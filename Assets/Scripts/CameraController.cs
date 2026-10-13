using UnityEngine;

/// <summary>
/// Управление камерой: орбитальное вращение по yaw (ПКМ + движение мыши по X)
/// и зум колесом мыши вдоль текущего направления камеры.
/// Повешен на Main Camera. Никаких других осей движения нет:
/// pitch (наклон ~60°), высота относительно pivot и позиция pivot по X/Z не меняются.
/// Pivot — точка на земле (y = 0), в которую камера смотрит в момент начала вращения.
/// Конфликт с InputController решён через IsRightMouseDragging: короткий клик ПКМ
/// без заметного движения мыши НЕ считается drag, отмена/снятие выделения работает как раньше.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Rotation (yaw, RMB + mouse X)")]
    [Tooltip("Градусы поворота на 1 пиксель движения мыши по X.")]
    public float rotationSpeed = 0.25f;

    [Tooltip("Порог накопленного смещения мыши по X (пиксели), после которого " +
             "ПКМ-drag считается вращением. До него клик ПКМ остаётся «отменой» для InputController.")]
    public float dragThresholdPixels = 6f;

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

    /// <summary>Наклон камеры (Euler.x) — фиксируется в Awake и больше не меняется.</summary>
    private float pitch;

    /// <summary>Текущий угол рыскания (Euler.y).</summary>
    private float yaw;

    private bool isRotating;          // идёт ли вращение сейчас (ПКМ зажата И порог превышен)
    private bool rmbDown;             // зажата ли ПКМ в этом кадре
    private float accumulatedMouseX;  // накопленная дельта мыши по X с момента нажатия ПКМ

    private Camera cam;

    /// <summary>
    /// True, если ПКМ зажата и мышь уже сдвинулась по X за порог — т.е. это drag
    /// для вращения камеры, а не короткий клик-отмена. Читается InputController.
    /// </summary>
    public static bool IsRightMouseDragging { get; private set; }

    private void Awake()
    {
        cam = GetComponent<Camera>();

        // Фиксируем текущую конфигурацию камеры как начальную орбиту:
        // наклон (pitch) берём из Euler.x, yaw — из Euler.y.
        Vector3 euler = transform.eulerAngles;
        pitch = euler.x;
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

        // --- Вращение по yaw (ПКМ + движение мыши по X) ---
        if (Input.GetMouseButtonDown(1))
        {
            rmbDown = true;
            isRotating = false;
            accumulatedMouseX = 0f;

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
            accumulatedMouseX += dx;

            if (!isRotating && Mathf.Abs(accumulatedMouseX) >= dragThresholdPixels)
            {
                // Порог превышен — теперь это drag-вращение.
                isRotating = true;
            }

            if (isRotating)
            {
                yaw += dx * rotationSpeed;
                ApplyCameraTransform();
            }
        }

        if (Input.GetMouseButtonUp(1))
        {
            rmbDown = false;
            isRotating = false;
            accumulatedMouseX = 0f;
        }

        IsRightMouseDragging = isRotating;
    }

    /// <summary>
    /// Переставить камеру на сфере радиуса distance вокруг pivot с фиксированным
    /// pitch и текущим yaw. Pivot, высота над ним и наклон не изменяются.
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
