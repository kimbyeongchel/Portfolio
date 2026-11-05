// 타입 검사 전용 최소 스텁 (CI에서 Unity 없이 Unity 스크립트를 컴파일해 보기 위함).
// 실제 Unity 동작을 흉내 내지 않는다. Unity 프로젝트에는 넣지 말 것.
using System;
namespace UnityEngine {
  public class Object { public static T Instantiate<T>(T original, Transform parent) where T : Object { return original; } }
  public class Component : Object { public Transform transform; public GameObject gameObject; }
  public class Behaviour : Component { public bool enabled; }
  public class MonoBehaviour : Behaviour { }
  public sealed class GameObject : Object { public void SetActive(bool v) { } }
  public class Transform : Component { public Vector3 position; public Quaternion rotation; public void SetPositionAndRotation(Vector3 p, Quaternion r) { } }
  [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
  public struct Vector2 { public float x, y; }
  public struct Vector3 {
    public float x, y, z;
    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3 zero { get { return new Vector3(0, 0, 0); } }
    public static Vector3 up { get { return new Vector3(0, 1, 0); } }
    public float magnitude { get { return (float)Math.Sqrt(x * x + y * y + z * z); } }
    public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
    public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
    public static Vector3 operator *(Vector3 a, float d) { return new Vector3(a.x * d, a.y * d, a.z * d); }
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { return a + (b - a) * t; }
  }
  public struct Quaternion {
    public static Quaternion Euler(float x, float y, float z) { return new Quaternion(); }
    public static Vector3 operator *(Quaternion q, Vector3 v) { return v; }
  }
  public static class Mathf {
    public static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
    public static int Clamp(int v, int a, int b) { return v < a ? a : v > b ? b : v; }
    public static int RoundToInt(float v) { return (int)Math.Round(v); }
    public static float Exp(float v) { return (float)Math.Exp(v); }
    public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
    public static float Max(float a, float b) { return a > b ? a : b; }
    public static float Repeat(float t, float l) { return t - (float)Math.Floor(t / l) * l; }
    public static float MoveTowards(float c, float t, float d) { return Math.Abs(t - c) <= d ? t : c + Math.Sign(t - c) * d; }
  }
  public static class Time { public static float deltaTime; }
  public class Animator : Behaviour {
    public static int StringToHash(string n) { return n.GetHashCode(); }
    public void SetFloat(int id, float v, float damp, float dt) { }
    public void SetBool(int id, bool v) { }
    public void SetInteger(int id, int v) { }
  }
  public class ParticleSystem : Component { public void Play() { } }
  public static class Debug { public static void LogError(object m, Object ctx) { } }
}
namespace UnityEngine.Pool {
  public class ObjectPool<T> where T : class {
    public ObjectPool(Func<T> create, Action<T> onGet = null, Action<T> onRelease = null, Action<T> onDestroy = null, bool collectionCheck = true, int defaultCapacity = 10, int maxSize = 10000) { }
    public T Get() { return null; } public void Release(T e) { }
  }
}
namespace UnityEngine.UI { public class Image : UnityEngine.Behaviour { public float fillAmount; } }
namespace UnityEngine.Animations.Rigging { public class Rig : UnityEngine.Behaviour { public float weight; } }
namespace UnityEngine.InputSystem {
  public class InputAction { public T ReadValue<T>() where T : struct { return default(T); } public bool IsPressed() { return false; } public bool WasPressedThisFrame() { return false; } }
  public class InputActionReference : UnityEngine.Object { public InputAction action; }
}
namespace TMPro { public class TMP_Text : UnityEngine.Behaviour { public void SetText(string s, float a) { } } }
