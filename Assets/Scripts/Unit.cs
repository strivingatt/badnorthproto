using UnityEngine;

/// <summary>
/// Юнит на сетке. Хранит логическую позицию, направление взгляда и принадлежность к отряду.
/// Полноценная реализация вместо ранее существовавшей заглушки.
/// </summary>
public class Unit : MonoBehaviour
{
    /// <summary>Индекс клетки сетки, которую занимает юнит.</summary>
    public Vector2Int GridPos;

    /// <summary>Направление взгляда (по умолчанию "вперёд" по оси Y сетки).</summary>
    public Vector2Int Facing = new Vector2Int(0, 1);

    /// <summary>Отряд, к которому принадлежит юнит.</summary>
    public Squad Squad;

    /// <summary>Идет ли сейчас анимация перемещения между клетками.</summary>
    public bool IsMoving;

    /// <summary>
    /// Инициализировать юнита: задать позицию и зарезервировать за ним клетку в GridManager.
    /// Если клетка уже занята/недоступна — логируем ошибку и не запускаем логику спавна.
    /// </summary>
    public void Init(Vector2Int cell, Squad squad)
    {
        GridPos = cell;
        Squad = squad;

        if (GridManager.Instance == null)
        {
            Debug.LogError("[Unit] GridManager.Instance == null — невозможно зарегистрировать юнита на сетке.", this);
            return;
        }

        if (!GridManager.Instance.TryOccupy(cell, this))
        {
            Debug.LogError($"[Unit] Клетка {cell} уже занята или недоступна — юнит не заспавнен, логика не запущена.", this);
            return;
        }

        // Позиционируем трансформ в центр клетки.
        transform.position = GridManager.Instance.GridToWorld(cell);

        // Логика появления юнита (анимация спавна, инициализация статов) появится позже.
    }

    /// <summary>
    /// Сменить материал у рендерера юнита (если он есть).
    /// </summary>
    public void SetVisual(Material mat)
    {
        if (mat == null)
        {
            Debug.LogWarning("[Unit] SetVisual: передан null-материал.", this);
            return;
        }

        var renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = mat;
        }
        else
        {
            Debug.LogWarning($"[Unit] На объекте \"{name}\" нет Renderer — материал не применён.", this);
        }
    }

    private void OnDestroy()
    {
        // При уничтожении освобождаем клетку, чтобы она снова стала доступной.
        if (GridManager.Instance != null)
        {
            GridManager.Instance.Free(GridPos, this);
        }
    }
}
