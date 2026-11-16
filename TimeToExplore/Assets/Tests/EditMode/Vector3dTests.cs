using NUnit.Framework;
using TimeToExplore.Core;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    public class Vector3dTests
    {
        [Test]
        public void BasicVectorAlgebraIsCorrect()
        {
            var a = new Vector3d(3, 4, 0);
            var b = new Vector3d(1, 2, 3);

            Assert.That(a.Magnitude, Is.EqualTo(5.0).Within(1e-12));
            Assert.That(a.SqrMagnitude, Is.EqualTo(25.0).Within(1e-12));
            Assert.That(Vector3d.Dot(a, b), Is.EqualTo(11.0).Within(1e-12));
            MathAssert.AreClose(Vector3d.Cross(Vector3d.Up, new Vector3d(1, 0, 0)),
                new Vector3d(0, 1, 0), 1e-12);
            Assert.That(Vector3d.Distance(a, new Vector3d(3, 0, 0)), Is.EqualTo(4.0).Within(1e-12));
        }

        [Test]
        public void NormalizingZeroDoesNotProduceNaN()
        {
            Assert.That(double.IsNaN(Vector3d.Zero.Normalized.x), Is.False);
            MathAssert.AreClose(Vector3d.Zero.Normalized, Vector3d.Zero, 0.0);
        }

        [Test]
        public void ToUnityPreservesChiralityAndMapsNorthToUp()
        {
            Vector3 unity = new Vector3d(1, 2, 3).ToUnity();
            Assert.That(unity.x, Is.EqualTo(1f));
            Assert.That(unity.y, Is.EqualTo(3f));
            Assert.That(unity.z, Is.EqualTo(-2f));

            MathAssert.AreClose(Vector3d.Up.ToUnity(), Vector3.up, 0.0);
        }

        [Test]
        public void FromUnityIsTheInverseOfToUnity()
        {
            var original = new Vector3d(1.5e10, -2.5e10, 3.5e10);
            MathAssert.AreClose(Vector3d.FromUnity(original.ToUnity()), original, 1e3);
        }

        [Test]
        public void ScalarOperatorsPreserveDirection()
        {
            var v = new Vector3d(1, -2, 3);
            MathAssert.AreClose(v * 2.0, new Vector3d(2, -4, 6), 0.0);
            MathAssert.AreClose(2.0 * v, new Vector3d(2, -4, 6), 0.0);
            MathAssert.AreClose(v / 2.0, new Vector3d(0.5, -1, 1.5), 0.0);
            MathAssert.AreClose(-v, new Vector3d(-1, 2, -3), 0.0);
        }
    }
}
