using UnityEngine;

/// <summary>
/// Управление камерой: орбитальное вращение по двум осям при зажатой ПКМ
/// (горизонтальное движение мыши → yaw, вертикальное → pitch) и зум колесом
/// мыши вдоль текущего направления камеры.
/// Повешен на Main Camera. Никаких других осей движения нет:
/// высота относительно pivot и позиция pivot по X/Z не меняются.
/// Pivot — точка на земле (y = 0), в которую камера смотрит в момент начала вращения.
/// Pitch ограничен диапазоном [minPitch, maxPitch], чтобы камера не уходила под землю
/// и не переворачивалась. Решение «клик ПКМ = снятие выделения» принимает сам
/// InputController на отпускании кнопки — этот класс больше не экспортирует флагов.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Rotation (yaw/pitch, RMB + mouse)")]
    [Tooltip("Градусы поворота по yaw на 1 единицу движения мыши по X.")]
    public float rotationSpeed = 0.25f;

    [Tooltip("Градусы наклона по pitch на 1 единицу движения мыши по Y.")]
    public float pitchSensitivity = 2.2f;

    [Tooltip("Минимальный pitch (градусы): почти горизонтальный взгляд. Не даёт камере уйти под землю.")]
    public float minPitch = 15f;

    [Tooltip("Максимальный pitch (градусы): почти вид сверху. Не даёт камере перевернуться.")]
    public float maxPitch = 75f;

    [Header("Zoom (mouse wheel)")]
    [Tooltip("Множитель дистанции на одну «щелчок» колеса (>1: вверх — приближение, вниз — отдаление).")]
    public float zoomSpeed = 1.1f;

    [Tooltip("Минимальная дистанция камеры до pivot (упор при приближении).")]
    public float minZoom = 8f;

    [Tooltip("Максимальная дистанция камеры до pivot (упор при отдалении).")]
    public float maxZoom = 60f;

    /// <summary>Точка фокуса на плоскости y = 0, вокруг которой орбиталит камера.</summary>
    private Vector3 pivot;

    /// <summary>Дистанция камеры до pivot (подбирается под стартовую конфигурацию в Awake).</summary>
    private float distance;

    /// <summary>Наклон камеры (Euler.x) — стартует с текущего угла, меняется drag'ом ПКМ, clamp'ится [minPitch, maxPitch].</summary>
    private float pitch;

    /// <summary>Текущий угол рыскания (Euler.y).</summary>
    private float yaw;

    private bool rmbDown; // зажата ли ПКМ в этом кадре (вращение применяется сразу, без порога)

    private Camera cam;

    private void Awake()
    {
        cam = GetComponent<Camera>();

        // Фиксируем текущую конфигурацию камеры как начальную орбиту:
        // наклон (pitch) берём из Euler.x (начальный pitch = текущий наклон), yaw — из Euler.y.
        Vector3 euler = transform.eulerAngles;
        pitch = Mathf.Clamp(euler.x > 180f ? euler.x - 360f : euler.x, minPitch, maxPitch);
        yaw = euler.y;

        // Pivot — пересечение луча взгляда камеры с плоскостью y = 0.
        if (!TryRaycastGround(transform.position, Quaternion.Euler(pitch, yaw, 0f), out pivot))
        {
            // Страховка: если камера почему-то не смотрит вниз — цель в начале координат.
            pivot = Vector3.zero;
            Debug.LogWarning("[CameraController] Луч камеры не пересекает плоскость y=0, " +
                             "pivot выставлен в (0,0,0).");
        }

        // Дистанция: фактическое расстояние до найденного pivot. Если оно вне
        // [minZoom, maxZoom] — clamp'им границы так, чтобы стартовый вид сохранился.
        distance = Vector3.Distance(transform.position, pivot);
        minZoom = Mathf.Min(minZoom, distance);
        maxZoom = Mathf.Max(maxZoom, distance);

        ApplyCameraTransform();
    }

    private void Update()
    {
        // --- Зум колесом (независимо от ПКМ) ---
        float scroll = Input.mouseScrollDelta.y;

        if (scroll > 0f)
        {
            // Приближение: уменьшаем дистанцию вдоль текущего направления камеры.
            distance = Mathf.Clamp(distance / zoomSpeed, minZoom, maxZoom);
            ApplyCameraTransform();
        }
        else if (scroll < 0f)
        {
            // Отдаление.
            distance = Mathf.Clamp(distance * zoomSpeed, minZoom, maxZoom);
            ApplyCameraTransform();
        }

        // --- Вращение по двум осям (ПКМ + движение мыши) ---
        if (Input.GetMouseButtonDown(1))
        {
            rmbDown = true;

            // В момент начала потенциального вращения заново определяем pivot:
            // точка на земле, на которую камера смотрит сейчас.
            if (TryRaycastGround(transform.position, transform.rotation, out Vector3 newPivot))
            {
                pivot = newPivot;
                distance = Vector3.Distance(transform.position, pivot);
                distance = Mathf.Clamp(distance, minZoom, maxZoom);
            }
        }

        if (rmbDown && Input.GetMouseButton(1))
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");

            // yaw: горизонтальное движение мыши → вращение вокруг Y (как раньше).
            yaw += dx * rotationSpeed;

            // pitch: вертикальное движение мыши → наклон по X.
            // Инверсия ( -= ): движение мыши вверх наклоняет камеру вниз.
            pitch -= dy * pitchSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            ApplyCameraTransform();
        }

        if (Input.GetMouseButtonUp(1))
        {
            rmbDown = false;
        }
    }

    /// <summary>
    /// Переставить камеру на сфере радиуса distance вокруг pivot с текущими
    /// pitch и yaw. Pivot и дистанция до него при вращении не изменяются.
    /// </summary>
    private void ApplyCameraTransform()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rotation * Vector3.forward; // направление «от pivot к камере»
        transform.position = pivot - offset * distance;
        transform.rotation = rotation;
    }

    /// <summary>
    /// Пересечение луча с началом в origin и заданным поворотом с плоскостью y = 0.
    /// Возвращает false, если луч параллелен плоскости или смотрит от неё.
    /// </summary>
    private static bool TryRaycastGround(Vector3 origin, Quaternion rotation, out Vector3 hit)
    {
        hit = Vector3.zero;
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        Ray ray = new Ray(origin, rotation * Vector3.forward);

        if (groundPlane.Raycast(ray, out float enter) && enter > 0f)
        {
            hit = ray.GetPoint(enter);
            return true;
        }

        return false;
    }
}
