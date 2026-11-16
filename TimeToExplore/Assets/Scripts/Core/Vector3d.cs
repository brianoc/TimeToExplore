using System;
using UnityEngine;

namespace TimeToExplore.Core
{
    /// <summary>
    /// Double-precision 3D vector. Unity's Vector3 is float and cannot hold
    /// solar-system-scale positions with useful precision, so all simulation
    /// maths uses this type.
    /// </summary>
    [Serializable]
    public struct Vector3d
    {
        public double x;
        public double y;
        public double z;

        public Vector3d(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static readonly Vector3d Zero = new Vector3d(0, 0, 0);
        public static readonly Vector3d Up = new Vector3d(0, 0, 1);

        public double SqrMagnitude => x * x + y * y + z * z;
        public double Magnitude => Math.Sqrt(SqrMagnitude);

        public Vector3d Normalized
        {
            get
            {
                double m = Magnitude;
                return m > 1e-30 ? new Vector3d(x / m, y / m, z / m) : Zero;
            }
        }

        public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3d operator -(Vector3d a) => new Vector3d(-a.x, -a.y, -a.z);
        public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.x * s, a.y * s, a.z * s);
        public static Vector3d operator *(double s, Vector3d a) => new Vector3d(a.x * s, a.y * s, a.z * s);
        public static Vector3d operator /(Vector3d a, double s) => new Vector3d(a.x / s, a.y / s, a.z / s);

        public static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static Vector3d Cross(Vector3d a, Vector3d b) => new Vector3d(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x);

        public static double Distance(Vector3d a, Vector3d b) => (a - b).Magnitude;

        public Vector3 ToVector3() => new Vector3((float)x, (float)y, (float)z);

        /// <summary>
        /// Ecliptic (x, y, z= north) to Unity (x, y=up, z=forward), preserving
        /// chirality. Unity's Y axis is the ecliptic north pole.
        /// </summary>
        public Vector3 ToUnity() => new Vector3((float)x, (float)z, (float)-y);

        /// <summary>Inverse of <see cref="ToUnity"/>; used to turn camera
        /// movement into an ecliptic-frame displacement.</summary>
        public static Vector3d FromUnity(Vector3 v) => new Vector3d(v.x, -v.z, v.y);

        public override string ToString() => $"({x:E3}, {y:E3}, {z:E3})";
    }
}
