using System;
using UnityEngine;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Player
{
    /// <summary>
    /// The map side of the zoom: how strongly the view has blended from the
    /// local orbit pose to a top-down map, which body sets the local scale, the
    /// extended zoom range, and the pan offset.
    ///
    /// Plain C# with no scene dependency, so the blend, the dominant-body
    /// choice, the limits, and the pan cap are verified in EditMode. The rig
    /// still owns the <see cref="OrbitCamera"/> and the player's pose; this
    /// object only reads them and never writes the player's yaw or pitch, so a
    /// zoom-out-and-back restores the exact angle.
    ///
    /// Distances arrive in render units because that is what
    /// <see cref="OrbitCamera.DistanceUnits"/> holds, while spheres of
    /// influence and body positions are double-precision metres. Conversion
    /// happens once, through <see cref="UnitsPerMeter"/>.
    /// </summary>
    public sealed class MapView
    {
        /// <summary>
        /// Map zoom ceiling: 12 billion km, about 80 AU. Frames Neptune's
        /// 4.5-billion-km orbit at 60° FOV with margin.
        /// </summary>
        public const float MaxDistanceUnits = 12_000_000f;

        /// <summary>The close end is the orbit camera's, unchanged: 2,000 km.</summary>
        public const float MinDistanceUnits = OrbitCamera.MinDistanceUnits;

        /// <summary>Pitch the map blends toward: straight down the ecliptic pole.</summary>
        public const float TopDownPitchDegrees = 90f;

        /// <summary>Fully top-down at this multiple of the local SOI radius.</summary>
        public const double BlendBandRatio = 3.0;

        /// <summary>
        /// Local scale when the ship is outside every sphere of influence:
        /// 1 million km, as the design specifies.
        /// </summary>
        public const double DeepSpaceRadiusM = 1.0e9;

        /// <summary>One astronomical unit in metres (IAU 2012 definition).</summary>
        public const double AstronomicalUnitM = 1.495978707e11;

        /// <summary>
        /// Pan limit: 60 AU from the ship, so the player cannot lose the
        /// system. The map zoom ceiling is 80 AU, which leaves the capped pan
        /// inside the framed catalogue.
        /// </summary>
        public const double MaxPanM = 60.0 * AstronomicalUnitM;

        private float _unitsPerMeter = 1e-6f;

        /// <summary>Render units per metre. The default is the scene's 1:1,000 km.</summary>
        public float UnitsPerMeter
        {
            get => _unitsPerMeter;
            set
            {
                if (!(value > 0f) || float.IsInfinity(value))
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "UnitsPerMeter must be positive and finite.");
                _unitsPerMeter = value;
            }
        }

        /// <summary>The map's look-at offset from the ship, in ecliptic metres.</summary>
        public Vector3d PanOffsetM { get; private set; }

        /// <summary>The same look-at offset in Unity render space, for the camera.</summary>
        public Vector3 PanOffsetUnits => (PanOffsetM * UnitsPerMeter).ToUnity();

        /// <summary>
        /// The body whose sphere of influence sets the local scale: the
        /// smallest finite SOI that contains the ship. At Gateway that is the
        /// Moon, whose SOI sits inside Earth's. Returns null in deep space.
        /// </summary>
        public static BodyRuntime DominantBody(SolarSystemModel model, Vector3d shipPositionM)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            BodyRuntime best = null;
            double bestRadiusM = double.PositiveInfinity;

            foreach (BodyRuntime body in model.Ordered)
            {
                double radiusM = SphereOfInfluence.RadiusM(body);

                // The Sun has no parent and therefore an infinite SOI; it is
                // never the local neighbourhood, and deep space has its own
                // fallback. A zero radius means a massless station or an
                // uninitialised body and cannot contain anything either.
                if (!(radiusM > 0.0) || double.IsInfinity(radiusM)) continue;
                if (radiusM >= bestRadiusM) continue;
                if (!SphereOfInfluence.Contains(body, shipPositionM, body.PositionM)) continue;

                best = body;
                bestRadiusM = radiusM;
            }

            return best;
        }

        /// <summary>
        /// The local scale `r`: the dominant body's SOI radius in metres, or
        /// <see cref="DeepSpaceRadiusM"/> when the ship is outside every SOI.
        /// </summary>
        public static double LocalRadiusM(SolarSystemModel model, Vector3d shipPositionM)
        {
            BodyRuntime dominant = DominantBody(model, shipPositionM);
            if (dominant == null) return DeepSpaceRadiusM;

            double radiusM = SphereOfInfluence.RadiusM(dominant);
            return double.IsFinite(radiusM) && radiusM > 0.0 ? radiusM : DeepSpaceRadiusM;
        }

        /// <summary>
        /// Blend weight from the camera distance and the local scale:
        /// `x = clamp(ln(d/r)/ln(3), 0, 1)`, `w = x²(3−2x)`. Zero is the local
        /// view, one is the top-down map.
        /// </summary>
        public float BlendWeight(float distanceUnits, double localRadiusM)
        {
            if (!(localRadiusM > 0.0) || double.IsInfinity(localRadiusM))
                throw new ArgumentOutOfRangeException(nameof(localRadiusM),
                    "The local scale must be positive and finite.");
            if (!(distanceUnits > 0f)) return 0f;

            double distanceM = distanceUnits / UnitsPerMeter;
            double ratio = distanceM / localRadiusM;

            // Exact at the endpoints rather than relying on the logarithm of a
            // ratio that unit conversion may have nudged a few ulps off 1.
            double x = ratio <= 1.0
                ? 0.0
                : ratio >= BlendBandRatio
                    ? 1.0
                    : Math.Log(ratio) / Math.Log(BlendBandRatio);

            return (float)(x * x * (3.0 - 2.0 * x));
        }

        /// <summary>
        /// The pitch the camera should use: the player's pitch eased toward
        /// straight down as the blend reaches the map. The player's own pitch
        /// is an input and is never modified, so w = 0 returns it unchanged.
        /// </summary>
        public float EffectivePitchDegrees(float playerPitchDegrees, float distanceUnits,
            double localRadiusM)
        {
            return Mathf.Lerp(playerPitchDegrees, TopDownPitchDegrees,
                BlendWeight(distanceUnits, localRadiusM));
        }

        /// <summary>Clamp an orbit distance to the map range (2,000 km to 12 billion km).</summary>
        public static float ClampDistanceUnits(float distanceUnits)
        {
            return Mathf.Clamp(distanceUnits, MinDistanceUnits, MaxDistanceUnits);
        }

        /// <summary>
        /// Multiplicative map zoom, clamped to the map range. The rig may use
        /// this instead of <see cref="OrbitCamera.Zoom"/> when the view is on
        /// the map side, whose ceiling is higher than the local camera's.
        /// </summary>
        public static float ZoomDistanceUnits(float distanceUnits, float steps,
            float factorPerStep = 1.25f)
        {
            if (factorPerStep <= 0f)
                throw new ArgumentOutOfRangeException(nameof(factorPerStep));

            if (steps == 0f) return ClampDistanceUnits(distanceUnits);

            double scaled = distanceUnits * Math.Pow(factorPerStep, steps);
            return ClampDistanceUnits((float)scaled);
        }

        /// <summary>
        /// Pan by a Unity-space delta in render units. The offset is held in
        /// ecliptic metres and capped, so a long drag cannot lose the system.
        /// </summary>
        public void Pan(Vector3 deltaUnits)
        {
            PanM(Vector3d.FromUnity(deltaUnits) / UnitsPerMeter);
        }

        /// <summary>Pan by an ecliptic offset in metres, capped at 60 AU.</summary>
        public void PanM(Vector3d deltaM)
        {
            SetPanM(PanOffsetM + deltaM);
        }

        /// <summary>Set the look-at offset, capped at 60 AU.</summary>
        public void SetPanM(Vector3d panM)
        {
            PanOffsetM = ClampPanM(panM);
        }

        /// <summary>Recentre the map on the ship.</summary>
        public void ClearPan()
        {
            PanOffsetM = Vector3d.Zero;
        }

        /// <summary>Clamp a look-at offset to the 60 AU pan cap.</summary>
        public static Vector3d ClampPanM(Vector3d panM)
        {
            double magnitude = panM.Magnitude;
            if (!(magnitude > MaxPanM)) return panM;
            return panM * (MaxPanM / magnitude);
        }
    }
}
