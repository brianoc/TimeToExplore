using System;
using System.IO;
using NUnit.Framework;
using TimeToExplore.Core;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>Shared access to the shipped catalogue and a fresh model.</summary>
    internal static class CatalogueFixture
    {
        private static string _json;
        private static SolarSystemFile _data;

        public static string Json
        {
            get
            {
                if (_json == null)
                {
                    string path = Path.Combine(
                        Application.dataPath, "Resources", "solar_system.json");
                    _json = File.ReadAllText(path);
                }

                return _json;
            }
        }

        public static SolarSystemFile Data
        {
            get
            {
                if (_data == null)
                    _data = JsonUtility.FromJson<SolarSystemFile>(Json);
                return _data;
            }
        }

        public static SolarSystemModel LoadModel()
        {
            var model = new SolarSystemModel();
            model.Load(Json);
            return model;
        }

        public static BodyData Find(string id)
        {
            foreach (BodyData body in Data.bodies)
            {
                if (body.id == id) return body;
            }

            Assert.Fail($"Catalogue has no body '{id}'.");
            return null;
        }
    }

    internal static class MathAssert
    {
        public static void AreClose(Vector3d actual, Vector3d expected, double tolerance)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance), "x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance), "y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance), "z");
        }

        public static void AreClose(Vector3 actual, Vector3 expected, double tolerance)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance), "x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance), "y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance), "z");
        }

        public static void AreClose(double actual, double expected, double tolerance, string message = null)
        {
            Assert.That(actual, Is.EqualTo(expected).Within(tolerance), message);
        }

        public static double WrapPi(double angle)
        {
            angle %= Math.PI * 2.0;
            if (angle < 0) angle += Math.PI * 2.0;
            return angle > Math.PI ? angle - Math.PI * 2.0 : angle;
        }
    }
}
