using UnityEngine;

/// <summary>
/// Рантайм-визуализация логической сетки GridManager.
/// Рисует линии сетки способом GL.LINES в OnRenderObject с назначенным
/// материалом — это позволяет использовать прозрачность (alpha), недоступную
/// для Gizmos (Gizmos — только Scene View редактора, здесь не используется).
///
/// Сетка видна ТОЛЬКО когда у игрока выделен свой отряд: Show()/Hide() вызывает
/// InputController из SetSelection. Никакой физики (Rigidbody/Collider) нет.
///
/// КООРДИНАТЫ: единственный источник правды — GridManager.GridToWorld().
/// Формула GridToWorld нигде НЕ дублируется и offset НЕ хардкодится: границы
/// сетки вычисляются из центра клетки (0,0) через public-поле cellSize
/// (GridToWorld возвращает ЦЕНТР клетки (cell + 0.5f) * cellSize — проверено
/// по коду GridManager, поэтому сдвиг на половину клетки обязателен).
/// Линии рисуются в МИРОВЫХ координатах (identity-матрица), чтобы позиция
/// GameObject-а рендерера в сцене не добавляла двойное смещение.
/// </summary>
public class GridRenderer : MonoBehaviour
{
    [Header("Grid Visual")]
    [Tooltip("Материал для линий сетки. Должен поддерживать прозрачность " +
             "(Unlit/Transparent, Sprites/Default и т.п.) — иначе альфа игнорируется.")]
    public Material lineMaterial;

    [Tooltip("Цвет линий. Прозрачность задаётся альфа-каналом; убедись, что " +
             "материал поддерживает прозрачность (Unlit/Transparent, Sprites/Default " +
             "и т.п.), иначе альфа игнорируется.")]
    public Color lineColor = new Color(1f, 1f, 1f, 0.3f);

    /// <summary>Стартовое состояние: сетка скрыта (видна только при выделении).</summary>
    [HideInInspector]
    public bool visible = false;

    private void Awake()
    {
        if (lineMaterial == null)
        {
            Debug.LogWarning("[GridRenderer] lineMaterial не назначен — сетка не будет отображаться.");
            visible = false;
        }
    }

    /// <summary>Показать сетку.</summary>
    public void Show()
    {
        visible = true;
    }

    /// <summary>Скрыть сетку.</summary>
    public void Hide()
    {
        visible = false;
    }

    /// <summary>
    /// Отрисовка линий сетки поверх сцены (вызывается камерой каждый кадр).
    /// Если сетка скрыта или материал не назначен — ничего не рисуем.
    /// Примечание: GL.LINES рисует линии фиксированной шириной 1 пиксель —
    /// регулировать толщину можно только шейдером/материалом.
    /// </summary>
    private void OnRenderObject()
    {
        if (!visible)
        {
            return;
        }

        if (lineMaterial == null || GridManager.Instance == null)
        {
            return;
        }

        // Применяем цвет (включая alpha) к материалу перед проходом.
        lineMaterial.SetColor("_Color", lineColor);

        DrawGridLines();
    }

    /// <summary>
    /// Построение линий сетки СТРОГО через публичный API GridManager
    /// (GridToWorld / size / cellSize) — без дублирования формулы преобразования.
    /// GridToWorld возвращает ЦЕНТР клетки, а линии идут по ГРАНИЦАМ клеток,
    /// поэтому нижний левый угол = центр клетки (0,0) минус половина клетки.
    /// </summary>
    private void DrawGridLines()
    {
        int size = GridManager.Instance.size;
        float cellSize = GridManager.Instance.cellSize;

        // Центр клетки (0,0) — из единственного источника координат.
        Vector3 firstCenter = GridManager.Instance.GridToWorld(new Vector2Int(0, 0));

        // GridToWorld отдаёт ЦЕНТР => угол клетки = центр - половина клетки.
        Vector3 origin = firstCenter - new Vector3(cellSize * 0.5f, 0f, cellSize * 0.5f);

        float worldSize = size * cellSize;

        GL.PushMatrix();

        // МИРОВЫЕ координаты: не используем transform объекта, иначе GameObject
        // рендерера не в (0,0,0) дал бы двойное смещение сетки относительно юнитов.
        GL.MultMatrix(Matrix4x4.identity);

        // Первый проход материала (для стандартных шейдеров — основной pass).
        lineMaterial.SetPass(0);

        GL.Begin(GL.LINES);

        for (int i = 0; i <= size; i++)
        {
            float t = i * cellSize;

            // Вертикальная линия (вдоль мировой Z) — по границам клеток.
            GL.Vertex3(origin.x + t, origin.y, origin.z);
            GL.Vertex3(origin.x + t, origin.y, origin.z + worldSize);

            // Горизонтальная линия (вдоль мировой X).
            GL.Vertex3(origin.x, origin.y, origin.z + t);
            GL.Vertex3(origin.x + worldSize, origin.y, origin.z + t);
        }

        GL.End();

        GL.PopMatrix();
    }
}
