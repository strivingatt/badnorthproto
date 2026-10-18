using UnityEngine;

/// <summary>
/// Воин (Warrior) — усиленный ближний бой. Наследник Militia: зона атаки
/// и удар остаются базовыми (3 смежные клетки перед собой), отличаются только
/// статы: больше HP и двойной урон.
/// </summary>
public class Warrior : Militia
{
    private void Awake()
    {
        maxHp = 7;
        attackDamage = 2;
        // attackCooldown остаётся базовым 0.8f (Militia).
    }
}
