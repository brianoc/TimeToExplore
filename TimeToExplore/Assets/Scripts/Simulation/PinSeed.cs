using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// The brake/departure fixed point's converged state, as a starting guess
    /// for a neighbouring build. A route's pin is one fixed point — the brake's
    /// duration and direction, the departure burn's duration and direction —
    /// and a build one slider step away converges to almost the same values.
    /// Starting from them saves the passes a cold build spends walking in from
    /// the kinematic brake estimate.
    ///
    /// The escape ray is deliberately **not** carried. The climb is derived
    /// from the time budget on every pass, and it is the ray that decides which
    /// basin the pin settles in; carrying it would change the answer rather
    /// than shorten the walk to it.
    ///
    /// A seed is a value type passed by value: applying one adds no allocation
    /// to the pin loop. It is only ever produced by a build that closed its
    /// chain and returned a plan, so a refusal or a stretched plan never seeds
    /// the next build.
    /// </summary>
    public readonly struct PinSeed
    {
        /// <summary>How far a seed may travel, as a fraction of the requested
        /// duration, before it is no longer a starting guess for it. A fifth
        /// covers the floor search's 20% growth step and the slider's whole
        /// track; beyond that the fixed point belongs to another trip.</summary>
        public const double MaxRelativeGap = 0.2;

        public PinSeed(double seconds, double brakeSeconds, Vector3d brakeDirection,
            double burnSeconds, Vector3d burnDirection, bool hasApproach)
        {
            Seconds = seconds;
            BrakeSeconds = brakeSeconds;
            BrakeDirection = brakeDirection;
            BurnSeconds = burnSeconds;
            BurnDirection = burnDirection;
            HasApproach = hasApproach;
        }

        /// <summary>The duration the fixed point converged at.</summary>
        public double Seconds { get; }

        public double BrakeSeconds { get; }
        public Vector3d BrakeDirection { get; }
        public double BurnSeconds { get; }
        public Vector3d BurnDirection { get; }

        /// <summary>True when the converged build flew the planetocentric
        /// approach. Pass 0 of a cold build deliberately drops it; a seeded
        /// build may keep it when the seeded brake is long enough.</summary>
        public bool HasApproach { get; }

        /// <summary>True when this value carries a usable guess. A
        /// <c>default(PinSeed)</c> is the cold start.</summary>
        public bool IsValid =>
            Seconds > 0.0 && double.IsFinite(Seconds)
            && BrakeSeconds > 0.0 && double.IsFinite(BrakeSeconds)
            && BurnSeconds > 0.0 && double.IsFinite(BurnSeconds)
            && BrakeDirection.SqrMagnitude > 0.5
            && BurnDirection.SqrMagnitude > 0.5;

        /// <summary>True when this seed may start a build at
        /// <paramref name="seconds"/>: valid, and within
        /// <see cref="MaxRelativeGap"/> of the duration it converged at.</summary>
        public bool Covers(double seconds) =>
            IsValid
            && seconds > 0.0
            && Math.Abs(seconds - Seconds) <= MaxRelativeGap * Seconds;

        /// <summary>This seed when it covers <paramref name="seconds"/>,
        /// otherwise the cold start.</summary>
        public PinSeed Nearby(double seconds) => Covers(seconds) ? this : default;
    }
}
