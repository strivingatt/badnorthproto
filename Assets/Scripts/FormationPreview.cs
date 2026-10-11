using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Превью целевой формации во время drag: пул из 32 «призрачных» плашек (ghost),
/// которые расставляются по клеткам-слотам через GridManager.GridToWorld.
/// </summary>
public class FormationPreview : MonoBehaviour
{
    private const int PoolSize = 32;

    [Header("Ghost Settings")]
    [Tooltip("Префаб плашки-«призрака» для превью слотов.")]
    public GameObject ghostPrefab;

    [Tooltip("Материал полупрозрачных плашек превью.")]
    public Material matGhost;

    /// <summary>Пул всех плашек (активные и неактивные).</summary>
    private readonly List<GameObject> pool = new List<GameObject>(PoolSize);

    private void Awake()
    {
        BuildPool();
    }

    /// <summary>Создать пул плашек и спрятать все.</summary>
    private void BuildPool()
    {
        if (ghostPrefab == null)
        {
            Debug.LogError("[FormationPreview] ghostPrefab не назначен — превью недоступно.", this);
            return;
        }

        for (int i = 0; i < PoolSize; i++)
        {
            var ghost = Instantiate(ghostPrefab, transform);
            ghost.name = $"Ghost_{i}";
            ghost.SetActive(false);

            if (matGhost != null)
            {
                var renderer = ghost.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = matGhost;
                }
            }

            pool.Add(ghost);
        }
    }

    /// <summary>
    /// Показать плашки по указанным слотам: первые slots.Count плашек активруются
    /// и позиционируются в центры клеток, остальные скрываются.
    /// </summary>
    public void Show(List<Vector2Int> slots)
    {
        if (GridManager.Instance == null || slots == null)
        {
            Hide();
            return;
        }

        int visible = Mathf.Min(slots.Count, pool.Count);

        if (slots.Count > pool.Count)
        {
            Debug.LogWarning($"[FormationPreview] Слотов больше пула ({slots.Count} > {pool.Count}) — показаны первые {pool.Count}.");
        }

        for (int i = 0; i < pool.Count; i++)
        {
            var ghost = pool[i];

            if (ghost == null)
            {
                continue;
            }

            if (i < visible)
            {
                ghost.transform.position = GridManager.Instance.GridToWorld(slots[i]);
                ghost.SetActive(true);
            }
            else
            {
                ghost.SetActive(false);
            }
        }
    }

    /// <summary>Спрятать все плашки превью.</summary>
    public void Hide()
    {
        foreach (var ghost in pool)
        {
            if (ghost != null)
            {
                ghost.SetActive(false);
            }
        }
    }
}
