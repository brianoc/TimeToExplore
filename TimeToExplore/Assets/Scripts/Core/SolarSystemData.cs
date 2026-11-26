using System;

namespace TimeToExplore.Core
{
    /// <summary>Mirrors Assets/Resources/solar_system.json.</summary>
    [Serializable]
    public class SolarSystemFile
    {
        public string name;
        public string epochUtc;
        public double epochJulianDate;
        public double startJulianDate;
        public string sourceNote;
        public BodyData[] bodies;
    }

    [Serializable]
    public class BodyData
    {
        public string id;
        public string name;
        public string kind;
        public string parent;
        public double radiusM;
        public double gm;
        public double rotationPeriodS;
        public double axialTiltDeg;
        public double poleRaDeg;
        public double poleDecDeg;
        public float[] color;
        public string description;
        public string albedoTexture;
        public AtmosphereData atmosphere;
        public OrbitData orbit;
        public LagrangeData lagrange;

        public bool IsStation => kind == "station";
        public bool IsStar => kind == "star";

        public bool HasAtmosphere => atmosphere != null && atmosphere.heightM > 0.0;
    }

    [Serializable]
    public class AtmosphereData
    {
        public double heightM;
        public float[] color;
        public float intensity;
    }

    [Serializable]
    public class OrbitData
    {
        public double aM;
        public double e;
        public double iDeg;
        public double raanDeg;
        public double argPeriDeg;
        public double meanAnomalyDeg;
        public double epochJulianDate;
        public string plane;

        public OrbitalElements ToElements(double parentGm)
        {
            return new OrbitalElements
            {
                semiMajorAxisM = aM,
                eccentricity = e,
                inclinationRad = iDeg * OrbitMath.Deg2Rad,
                ascendingNodeRad = raanDeg * OrbitMath.Deg2Rad,
                argumentOfPeriapsisRad = argPeriDeg * OrbitMath.Deg2Rad,
                meanAnomalyRad = meanAnomalyDeg * OrbitMath.Deg2Rad,
                epochJulianDate = epochJulianDate,
                gravitationalParameter = parentGm,
                parentEquatorFrame = plane == "parentEquator"
            };
        }
    }

    [Serializable]
    public class LagrangeData
    {
        public string primary;
        public string secondary;
        public string point;

        public LagrangePoint Point
        {
            get
            {
                switch (point)
                {
                    case "L2": return LagrangePoint.L2;
                    case "L4": return LagrangePoint.L4;
                    case "L5": return LagrangePoint.L5;
                    default: return LagrangePoint.L1;
                }
            }
        }
    }
}
