using System.Text.Json;

namespace UnityEngine
{
    // Only the installer's version-document deserialization is needed outside Unity.
    public static class JsonUtility
    {
        public static T FromJson<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { IncludeFields = true });
        }
    }
}
