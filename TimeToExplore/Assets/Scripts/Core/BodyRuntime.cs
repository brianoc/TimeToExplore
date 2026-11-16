using System;
using System.Collections.Generic;

namespace TimeToExplore.Core
{
    /// <summary>
    /// Runtime state for one body. Positions are heliocentric, ecliptic J2000,
    /// in metres, and are recomputed from Keplerian elements every frame.
    /// </summary>
    public class BodyRuntime
    {
        public readonly BodyData Data;
        public readonly string Id;
        public readonly bool HasOrbit;
        public OrbitalElements Elements;

        public BodyRuntime Parent;
        public readonly List<BodyRuntime> Children = new List<BodyRuntime>();

        public Vector3d PositionM;
        public Vector3d VelocityMps;

        public Vector3d PoleEcliptic;
        public Vector3d EquatorX;
        public Vector3d EquatorY;

        /// <summary>Used by Lagrange stations to derive a velocity from the
        /// movement of their point between simulation steps.</summary>
        public double LastJulianDate = double.NaN;
        public Vector3d LastPositionM;

        public BodyRuntime(BodyData data)
        {
            Data = data;
            Id = data.id;
            // JsonUtility turns a JSON null object into a default instance, so
            // `orbit != null` alone would give the Sun and stations an orbit.
            // A body only orbits if it has a parent and is not a station.
            HasOrbit = data.orbit != null
                       && !string.IsNullOrEmpty(data.parent)
                       && !data.IsStation;
            PoleEcliptic = OrbitMath.PoleFromRaDec(data.poleRaDeg, data.poleDecDeg);

            // Build a stable basis in the parent's equatorial plane. The choice
            // of x-axis is arbitrary because moon epoch phases are authored.
            Vector3d reference = Math.Abs(PoleEcliptic.z) < 0.9
                ? Vector3d.Up
                : new Vector3d(1, 0, 0);
            EquatorX = Vector3d.Cross(reference, PoleEcliptic).Normalized;
            EquatorY = Vector3d.Cross(PoleEcliptic, EquatorX).Normalized;
        }

        public string Name => Data.name;
        public string Kind => Data.kind;
        public double RadiusM => Data.radiusM;
        public double Gm => Data.gm;
        public bool IsRenderable => Data.kind != "barycentre";

        public void InitialiseElements()
        {
            if (!HasOrbit) return;
            double mu = Parent != null ? Parent.Gm : 0.0;
            Elements = Data.orbit.ToElements(mu);
        }

        public void UpdateOrbit(double julianDate)
        {
            Elements.GetState(julianDate, out Vector3d localPosition, out Vector3d localVelocity);

            if (Elements.parentEquatorFrame && Parent != null)
            {
                localPosition = Parent.FromEquator(localPosition);
                localVelocity = Parent.FromEquator(localVelocity);
            }

            PositionM = (Parent?.PositionM ?? Vector3d.Zero) + localPosition;
            VelocityMps = (Parent?.VelocityMps ?? Vector3d.Zero) + localVelocity;
        }

        /// <summary>Transform a vector from this body's equatorial frame to the ecliptic frame.</summary>
        public Vector3d FromEquator(Vector3d local) =>
            EquatorX * local.x + EquatorY * local.y + PoleEcliptic * local.z;

        public double SpinAngleRadians(double julianDate)
        {
            if (Data.rotationPeriodS == 0.0) return 0.0;
            double epoch = HasOrbit ? Data.orbit.epochJulianDate : JulianDate.J2000;
            double seconds = (julianDate - epoch) * JulianDate.SecondsPerDay;
            return OrbitMath.WrapTwoPi(seconds / Data.rotationPeriodS * OrbitMath.TwoPi);
        }

        public double HillRadiusM
        {
            get
            {
                if (Parent == null || Parent.Gm <= 0.0) return double.PositiveInfinity;
                double distance = Vector3d.Distance(PositionM, Parent.PositionM);
                double ratio = Gm / (3.0 * Parent.Gm);
                return distance * Math.Cbrt(ratio);
            }
        }
    }
}
