using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Отряд юнитов. Пока только список составных частей и добавление в него —
/// команды движения появятся позже.
/// </summary>
public class Squad : MonoBehaviour
{
    /// <summary>Юниты, входящие в отряд.</summary>
    public List<Unit> units = new List<Unit>();

    /// <summary>Добавить юнит в отряд (null игнорируется).</summary>
    public void Add(Unit u)
    {
        if (u == null)
        {
            Debug.LogWarning("[Squad] Add: unit == null — пропущено.");
            return;
        }

        if (!units.Contains(u))
        {
            units.Add(u);
        }
    }
}
