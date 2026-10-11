using UnityEngine;

/// <summary>
/// Обрабатывает пользовательский ввод: выбор отряда кликом мыши по сетке.
/// Рейкаст луча из мыши на математическую плоскость y = 0, определение клетки под курсором,
/// выделение отряда юнита, находящегося в этой клетке, и подсветка выбранного отряда.
/// Синглтон — доступ через InputController.Instance.
/// Drag (перетаскивание отряда) будет добавлен следующим шагом — заготовки методов ниже.
/// </summary>
public class InputController : MonoBehaviour
{
    public static InputController Instance { get; private set; }

    [Header("Visuals")]
    [Tooltip("Материал для подсветки выбранного отряда.")]
    public Material matSelected;

    [Tooltip("Обычный материал юнитов. Если не назначен, берётся из SquadSpawner.matUnit.")]
    public Material matUnit;

    [Tooltip("Спавнер, у которого можно взять базовый материал юнитов (необязательно).")]
    public SquadSpawner spawner;

    /// <summary>Отряд, выбранный в данный момент (null — ничего не выбрано).</summary>
    public Squad SelectedSquad { get; private set; }

    /// <summary>Математическая плоскость y = 0 для рейкаста мыши.</summary>
    private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

    private Camera mainCamera;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[InputController] Дубликат экземпляра удалён.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("[InputController] Камера с тегом MainCamera не найдена.");
        }

        // Если matUnit не назначен вручную — берём его из поля спавнера.
        if (matUnit == null)
        {
            if (spawner == null)
            {
                spawner = FindObjectOfType<SquadSpawner>();
            }

            if (spawner != null)
            {
                matUnit = spawner.matUnit;
            }
        }
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            HandleClick();
        }

        // Drag — следующим шагом подключим Input.GetMouseButton(0/1) к заготовкам ниже.
    }

    /// <summary>Клик ЛКМ: определяем клетку под курсором и выделяем отряд юнита в ней.</summary>
    private void HandleClick()
    {
        if (mainCamera == null || GridManager.Instance == null)
        {
            return;
        }

        Vector2Int cell = GetCellUnderCursor(out bool ok);
        if (!ok)
        {
            return;
        }

        Unit unit = GridManager.Instance.GetOccupant(cell);

        // Меняем ли мы выделение?
        Squad newSquad = unit != null ? unit.Squad : null;

        if (newSquad == SelectedSquad)
        {
            return; // выделение не изменилось — перекраску не делаем
        }

        SetSelection(newSquad);
    }

    /// <summary>
    /// Рейкаст луча из позиции мыши на плоскость y = 0.
    /// Возвращает клетку сетки под курсором; ok = false, если луч не попал в плоскость.
    /// </summary>
    private Vector2Int GetCellUnderCursor(out bool ok)
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            ok = true;
            return GridManager.Instance.WorldToGrid(hitPoint);
        }

        ok = false;
        return default;
    }

    /// <summary>
    /// Смена выделения: старый отряд возвращается к matUnit, новый подсвечивается matSelected.
    /// </summary>
    private void SetSelection(Squad newSquad)
    {
        // Снимаем подсветку со старого отряда.
        if (SelectedSquad != null)
        {
            ApplyMaterial(SelectedSquad, matUnit);
        }

        SelectedSquad = newSquad;

        // Подсвечиваем новый отряд.
        if (SelectedSquad != null)
        {
            ApplyMaterial(SelectedSquad, matSelected);
        }
    }

    /// <summary>Применить материал ко всем юнитам отряда (null-материал пропускается).</summary>
    private void ApplyMaterial(Squad squad, Material mat)
    {
        if (squad == null || mat == null)
        {
            return;
        }

        foreach (var unit in squad.units)
        {
            if (unit != null)
            {
                unit.SetVisual(mat);
            }
        }
    }

    // ------------------------------------------------------------------
    // Заготовки под drag (будут реализованы следующим шагом):
    // ------------------------------------------------------------------

    /// <summary>Начало перетаскивания выбранного отряда (ЛКМ зажато над юнитом).</summary>
    private void OnDragStart()
    {
        // TODO (следующий шаг): зафиксировать стартовую клетку, показать превью смещения.
    }

    /// <summary>Обновление перетаскивания: курсор движется, считаем целевую формацию.</summary>
    private void OnDragUpdate()
    {
        // TODO (следующий шаг): клетка под курсором -> offset от точки захвата -> подсветка целевых клеток.
    }

    /// <summary>Завершение перетаскивания (отпускание ЛКМ): валидация и приказ на движение.</summary>
    private void OnDragEnd()
    {
        // TODO (следующий шаг): проверить доступность целевых клеток, отдать отряду команду движения.
    }
}
