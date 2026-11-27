using System;
using System.Text.Json;

namespace UnityEngine
{
    /// <summary>The only UnityEngine type the planner sources touch.</summary>
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public static class Debug
    {
        public static void Log(object message) => Console.Error.WriteLine(message);

        public static void LogWarning(object message) =>
            Console.Error.WriteLine("warning: " + message);
    }

    /// <summary>The benchmark loads the catalogue from disk, so no resource is
    /// ever present through the shim.</summary>
    public class TextAsset
    {
        public string text;
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : class => null;
    }

    /// <summary>
    /// The shim for Unity's field-only JSON reader. The planner's data classes
    /// use public fields, which System.Text.Json reads with IncludeFields; the
    /// computed properties Unity ignores stay out.
    /// </summary>
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true
        };

        public static T FromJson<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, Options);
    }
}
