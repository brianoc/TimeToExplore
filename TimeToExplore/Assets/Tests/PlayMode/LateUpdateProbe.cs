using System;
using UnityEngine;

namespace TimeToExplore.Tests.PlayMode
{
    /// <summary>
    /// Runs a callback at the end of a frame, after the bootstrap's own
    /// LateUpdate. A <c>[UnityTest]</c> coroutine resumes in the Update phase,
    /// so this is how a test reads state the bootstrap drew after that resume
    /// point.
    /// </summary>
    internal sealed class LateUpdateProbe : MonoBehaviour
    {
        public Action Callback;

        private void LateUpdate()
        {
            Callback?.Invoke();
        }
    }
}
