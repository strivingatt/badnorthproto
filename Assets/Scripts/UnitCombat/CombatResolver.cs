using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Шаг 8 (боевая система MVP): глобальный тактовый разрешитель боя.
/// Периодически (tickInterval) обходит ВСЕ юниты на сетке: сначала тикает
/// кулдауны всех, затем каждый атакувший юнит пытается нанести удар
/// (Unit.TryAttack). Два прохода гарантируют, что порядок итерации по
/// словарю occupancy не даёт никому преимущества по времени атаки.
/// </summary>
public class CombatResolver : MonoBehaviour
{
    [Header("Combat")]
    /// <summary>Период боевого тика, секунд.</summary>
    public float tickInterval = 0.1f;

    /// <summary>Накопитель времени до следующего тика.</summary>
    private float timer = 0f;

    private void Update()
    {
        var gm = GridManager.Instance;

        if (gm == null)
        {
            return;
        }

        timer += Time.deltaTime;

        if (timer < tickInterval)
        {
            return;
        }

        // Вычитаем интервал, а не обнуляем таймер — тики не «дрейфуют»,
        // если кадр длиннее tickInterval.
        timer -= tickInterval;

        // Два прохода: сначала все кулдауны тикают, потом все атакуют
        // (порядок юнитов не даёт преимущества).
        List<Unit> units = gm.GetAllUnits();

        // Исправление бага MissingReferenceException: в occupancy могли остаться
        // «висячие» ссылки на уничтоженных юнитов — фильтруем живых перед обоими
        // проходами.
        List<Unit> aliveUnits = units.FindAll(u => u != null && u.gameObject != null);

        // Проход 1: тик кулдаунов.
        foreach (var unit in aliveUnits)
        {
            if (unit != null)
            {
                unit.cooldownTimer = Mathf.Max(0f, unit.cooldownTimer - tickInterval);
            }
        }

        // Проход 2: атаки (итерируем по копии, юниты могут умирать).
        List<Unit> attackers = new List<Unit>(aliveUnits);

        foreach (var unit in attackers)
        {
            // Умершие в этом же тике юниты (Destroy отложен до конца кадра,
            // но ссылка уже «null-обёртка») и движущиеся не атакуют.
            if (unit != null && !unit.IsMoving)
            {
                unit.TryAttack();
            }
        }
    }
}
