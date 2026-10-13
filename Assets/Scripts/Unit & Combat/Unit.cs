using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Юнит на сетке. Хранит логическую позицию, направление взгляда и принадлежность
/// к отряду. Движение — по клеткам пути A* (обход заблокированных клеток).
///
/// ВИЗУАЛ: модель может содержать несколько рендереров (MeshRenderer тела,
/// SkinnedMeshRenderer меча и т.п.). Перекраска (SetVisual) и вспышка урона
/// (Flash) работают с кэшем всех рендереров модели (rendererCache), нос
/// (объект "Nose") исключён.
///
/// АНИМАЦИЯ: Animator опционален (может отсутствовать у старых префабов-кубов).
/// Используется ТОЛЬКО для триггера атаки: SetTrigger("Attack").
/// </summary>
public class Unit : MonoBehaviour
{
    /// <summary>Индекс клетки сетки, которую занимает юнит.</summary>
    public Vector2Int GridPos;

    /// <summary>Направление взгляда (по умолчанию «вперёд» по оси Y сетки = мир +Z).</summary>
    public Vector2Int Facing = new Vector2Int(0, 1);

    /// <summary>Отряд, к которому принадлежит юнит.</summary>
    public Squad Squad;

    /// <summary>Идет ли сейчас движение между клетками.</summary>
    public bool IsMoving;

    /// <summary>Скорость перемещения в клетках в секунду (Vector3.MoveTowards по клеткам пути A*).</summary>
    public float moveSpeed = 4f;

    // --- Боевые характеристики (Шаг 8) ---

    /// <summary>Команда юнита.</summary>
    public Team team = Team.Player;

    /// <summary>Максимальное здоровье.</summary>
    public int maxHp = 3;

    /// <summary>Текущее здоровье (только чтение извне).</summary>
    private int hp;
    public int Hp => hp;

    /// <summary>Урон одной атаки.</summary>
    public int attackDamage = 1;

    /// <summary>Длительность перезарядки атаки, секунд.</summary>
    public float attackCooldown = 0.8f;

    /// <summary>Оставшееся время до следующей атаки, секунд (тикает CombatResolver).</summary>
    public float cooldownTimer = 0f;

    /// <summary>Материал вспышки при получении урона (назначается спавнером).</summary>
    public Material matHit;

    /// <summary>Активная корутина вспышки «получил урон».</summary>
    private Coroutine flashCoroutine;

    /// <summary>Активная корутина движения (чтобы останавливать при повторном приказе).</summary>
    private Coroutine moveCoroutine;

    /// <summary>Дочерний объект-указатель направления («нос»).</summary>
    private Transform noseTransform;

    /// <summary>Последняя зафиксированная клетка (для синхронизации GridPos без физики).</summary>
    private Vector2Int lastGridPos;

    /// <summary>«Родной» материал юнита (белый у игроков, красный у врагов):
    /// запоминается при спавне, восстанавливается после снятия выделения.</summary>
    private Material defaultMaterial;

    /// <summary>
    /// Пары «рендерер → его текущий оригинальный материал» для возврата
    /// после вспышки урона. Строится в Init() через BuildRendererCache()
    /// ПОСЛЕ создания носа; нос в кэш не входит.
    /// </summary>
    private readonly List<(Renderer renderer, Material original)> rendererCache
        = new List<(Renderer, Material)>();

    /// <summary>
    /// Animator модели (может быть null — у старых префабов-кубов аниматора нет).
    /// Используется только для триггера атаки: SetTrigger("Attack").
    /// </summary>
    private Animator animator;

    // --- Авто-поворот к ближайшему врагу ---

    /// <summary>Радиус обнаружения врагов для авто-поворота, в клетках.</summary>
    public float detectionRange = 10f;

    /// <summary>Таймер пересчёта направления взгляда.</summary>
    private float facingUpdateTimer = 0f;

    /// <summary>Интервал пересчёта направления, секунд (не каждый кадр).</summary>
    private const float FacingUpdateInterval = 0.2f;

    /// <summary>
    /// Таблица 8 секторов направлений (тот же порядок, что в Formation.cs):
    /// индекс 0 = Восток (+X), далее против часовой: NE, N(+Y), NW, W, SW, S, SE.
    /// </summary>
    private static readonly Vector2Int[] Sectors =
    {
        new Vector2Int(1, 0),
        new Vector2Int(1, 1),
        new Vector2Int(0, 1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1, 0),
        new Vector2Int(-1, -1),
        new Vector2Int(0, -1),
        new Vector2Int(1, -1),
    };

    /// <summary>
    /// Инициализировать юнита: задать позицию и зарезервировать за ним клетку
    /// в GridManager. Если клетка уже занята/недоступна — логируем ошибку и
    /// НЕ запускаем логику спавна.
    /// </summary>
    public void Init(Vector2Int cell, Squad squad)
    {
        GridPos = cell;
        Squad = squad;
        hp = maxHp;

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

        // Фиксируем стартовую клетку для синхронизации позиции в FixedUpdate.
        lastGridPos = cell;

        // Создаём «нос» child-объектом КОДОМ (не через префаб) — визуальный
        // указатель направления. ВАЖНО: до BuildRendererCache, чтобы нос
        // успел появиться и был отфильтрован по имени.
        CreateNose();

        // Применяем текущее направление: локальный +Z юнита смотрит вдоль Facing.
        ApplyFacing();

        // Кэшируем все рендереры модели (тело + меч и т.п.), кроме носа.
        BuildRendererCache();

        // Animator может отсутствовать (старые префабы-кубы) — это допустимо,
        // анимация атаки просто не проиграется.
        animator = GetComponent<Animator>();

        // ИИ врагов: если это вражеский юнит — навешиваем EnemyAI кодом
        // (компонент появляется автоматически сразу после регистрации на сетке).
        if (team == Team.Enemy && GetComponent<EnemyAI>() == null)
        {
            gameObject.AddComponent<EnemyAI>();
        }
    }

    /// <summary>
    /// Собрать все рендереры модели (MeshRenderer, SkinnedMeshRenderer и т.п.),
    /// исключив объект "Nose". Для каждого сохранить его sharedMaterial как
    /// «оригинал» для возврата после вспышки урона.
    /// includeInactive = true — на случай, если часть модели временно отключена.
    /// </summary>
    private void BuildRendererCache()
    {
        rendererCache.Clear();

        Renderer[] all = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer r in all)
        {
            if (r == null) continue;

            // Исключаем нос (указатель направления) — он не перекрашивается.
            if (r.gameObject.name == "Nose") continue;

            rendererCache.Add((r, r.sharedMaterial));
        }

        if (rendererCache.Count == 0)
        {
            Debug.LogWarning($"[Unit] {name}: не найдено ни одного рендерера (кроме носа) — " +
                             $"перекраска и вспышка урона работать не будут.", this);
        }
    }

    private void Update()
    {
        // Авто-поворот: пересчёт направления не каждый кадр, а с интервалом 0.2с.
        facingUpdateTimer += Time.deltaTime;

        if (facingUpdateTimer >= FacingUpdateInterval)
        {
            facingUpdateTimer = 0f;
            UpdateFacing();
        }
    }

    /// <summary>
    /// Найти ближайшего живого врага (team != this.team) в радиусе detectionRange.
    /// Расстояние считаем в клетках (евклидово, целочисленно — без GridToWorld).
    /// Возвращает Unit или null.
    /// </summary>
    public Unit FindNearestEnemy()
    {
        var gm = GridManager.Instance;

        if (gm == null)
        {
            return null;
        }

        Unit nearest = null;
        float bestDistSqr = detectionRange * detectionRange;

        List<Unit> all = gm.GetAllUnits();

        for (int i = 0; i < all.Count; i++)
        {
            Unit u = all[i];

            // Мёртвый/уничтоженный юнит (unity-null) или своя команда — пропускаем.
            if (u == null || u.gameObject == null || u.team == this.team)
            {
                continue;
            }

            // Расстояние в клетках (целочисленно). detectionRange трактуется
            // в клетках, а не в мировых единицах — не зависит от cellSize.
            int dx = u.GridPos.x - GridPos.x;
            int dy = u.GridPos.y - GridPos.y;
            float distSqr = dx * dx + dy * dy;

            if (distSqr < bestDistSqr)
            {
                bestDistSqr = distSqr;
                nearest = u;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Пересчитать направление взгляда: развернуть юнита лицом к ближайшему
    /// врагу (если тот есть в радиусе обнаружения). Направление квантуется до
    /// 8 секторов. Если врага нет — Facing не меняется.
    /// </summary>
    public void UpdateFacing()
    {
        Unit enemy = FindNearestEnemy();

        if (enemy == null)
        {
            return; // врагов рядом нет — сохраняем текущий Facing
        }

        Vector2Int dir = enemy.GridPos - GridPos;

        // Совпадение клеток невозможно, но защита от нулевого вектора нужна:
        // Atan2(0,0) вернёт 0 и юнит развернулся бы без причины.
        if (dir == Vector2Int.zero)
        {
            return;
        }

        // Квантование до 8 секторов. Таблица Sectors обходит по часовой от +X,
        // поэтому угол берём как Atan2(dir.y, dir.x) — от +X к +Y.
        float angle = Mathf.Atan2(dir.y, dir.x);
        int k = Mathf.RoundToInt(angle / (Mathf.PI * 0.25f));
        k = ((k % 8) + 8) % 8;

        Vector2Int newFacing = Sectors[k];

        if (newFacing != Facing)
        {
            Facing = newFacing;
            ApplyFacing();
        }
    }

    /// <summary>
    /// Синхронизация логической позиции с фактической (БЕЗ физики): каждый
    /// FixedUpdate читаем клетку под transform.position и, если юнит вошёл в
    /// новую клетку, обновляем GridPos и occupancy. Юниты не блокируют друг
    /// друга — клетка перезаписывается последним вошедшим.
    /// </summary>
    private void FixedUpdate()
    {
        if (GridManager.Instance == null) return;

        Vector2Int currentCell = GridManager.Instance.WorldToGrid(transform.position);

        // Deadzone: не фиксируем смену клетки, пока юнит не подъехал
        // достаточно близко к её центру.
        Vector3 currentWorldPos = GridManager.Instance.GridToWorld(currentCell);
        float distToCenter = Vector3.Distance(transform.position, currentWorldPos);
        if (distToCenter > 0.4f) return;

        if (currentCell != GridPos && GridManager.Instance.InBounds(currentCell))
        {
            GridManager.Instance.Free(GridPos, this);
            GridManager.Instance.ForceOccupy(currentCell, this); // перезаписываем, кого там ни было
            GridPos = currentCell;
        }
    }

    /// <summary>
    /// Создать child-объект "Nose" кодом (не префабом): маленький куб
    /// localScale (0.2, 0.2, 0.4), localPosition (0, 0, 0.45) — выступает
    /// вперёд по локальному +Z юнита. Родитель — трансформ юнита.
    /// Вызывается один раз из Init.
    /// </summary>
    private void CreateNose()
    {
        if (noseTransform != null)
        {
            return; // уже создан
        }

        var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "Nose";
        nose.transform.SetParent(transform, false);
        nose.transform.localScale = new Vector3(0.2f, 0.2f, 0.4f);
        nose.transform.localPosition = new Vector3(0f, 0f, 0.45f);

        noseTransform = nose.transform;
    }

    /// <summary>
    /// Повернуть трансформ юнита так, чтобы его локальный +Z смотрел вдоль
    /// вектора Facing в мировых координатах.
    /// Соответствие: Facing (0,1) -> мир +Z; (1,0) -> мир +X; (0,-1) -> мир -Z;
    /// (-1,0) -> мир -X; диагонали -> 45 градусов.
    /// </summary>
    public void ApplyFacing()
    {
        float angle = Mathf.Atan2(Facing.x, Facing.y) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }

    /// <summary>
    /// Сменить материал у ВСЕХ рендереров кэша (тело + меч и т.п.).
    /// Обновляет «оригиналы» в кэше, чтобы вспышка урона возвращала актуальный
    /// цвет (например, после подсветки выделения).
    /// </summary>
    public void SetVisual(Material mat)
    {
        if (mat == null)
        {
            Debug.LogWarning("[Unit] SetVisual: передан null-материал.", this);
            return;
        }

        for (int i = 0; i < rendererCache.Count; i++)
        {
            var (r, _) = rendererCache[i];

            if (r != null)
            {
                r.material = mat;
                rendererCache[i] = (r, mat);
            }
        }
    }

    /// <summary>
    /// Назначить «родной» материал юнита и сразу применить его.
    /// Вызывается при спавне (SquadSpawner) — чтобы юнит помнил свой исходный
    /// цвет и мог восстановить его после снятия выделения.
    /// </summary>
    public void SetDefaultVisual(Material mat)
    {
        defaultMaterial = mat;
        SetVisual(mat);
    }

    /// <summary>
    /// Восстановить «родной» материал (снять подсветку выделения).
    /// Если родной материал не задан — ничего не делаем.
    /// </summary>
    public void RestoreDefaultVisual()
    {
        if (defaultMaterial != null)
        {
            SetVisual(defaultMaterial);
        }
    }

    /// <summary>
    /// Клетки зоны атаки: 3 клетки фронта прямо перед юнитом
    /// (центр — GridPos + Facing, плюс ±1 по перпендикуляру «вправо»).
    ///
    /// Пример расчёта для Facing=(0,1): right = (Facing.y, -Facing.x) = (1,0);
    /// клетки: (x-1,y+1), (x,y+1), (x+1,y+1) — столбец на y+1 шириной 3.
    /// </summary>
    public List<Vector2Int> GetAttackCells()
    {
        Vector2Int right = new Vector2Int(Facing.y, -Facing.x); // перпендикуляр вправо
        List<Vector2Int> cells = new List<Vector2Int>();

        for (int r = -1; r <= 1; r++)
        {
            Vector2Int cell = GridPos + Facing * 1 + right * r;
            cells.Add(cell);
        }

        return cells;
    }

    /// <summary>
    /// Попытка атаки: цели — активные враги команды в зоне атаки.
    /// Кулдаун запускается только при наличии целей; при успешной атаке
    /// дёргается триггер анимации "Attack".
    /// </summary>
    public bool TryAttack()
    {
        var gm = GridManager.Instance;
        if (gm == null) return false;

        List<Unit> targets = new List<Unit>();

        foreach (Vector2Int cell in GetAttackCells())
        {
            if (!gm.InBounds(cell)) continue;

            Unit occupant = gm.GetOccupant(cell);

            // Проверяем, что враг активен (not destroyed) — иначе мёртвый юнит
            // мог формально оставаться в клетке.
            if (occupant != null && occupant.team != this.team && occupant.gameObject != null)
            {
                targets.Add(occupant);
            }
        }

        if (targets.Count == 0) return false;

        cooldownTimer = attackCooldown;

        // Триггер (не Play/bool) — повторные атаки корректно перезапускают клип.
        // animator может быть null у старых префабов-кубов.
        if (animator != null)
        {
            animator.SetTrigger("Attack");
        }

        Debug.Log($"[Combat] {name} бьёт {targets.Count} целей на {attackDamage} урона");

        foreach (var target in targets)
        {
            target.TakeDamage(attackDamage, this);
        }

        return true;
    }

    /// <summary>
    /// Получить урон: минус HP, вспышка ВСЕХ рендереров, смерть при HP <= 0.
    /// При цепочке быстрых ударов: перед остановкой старой корутины
    /// восстанавливаем оригиналы вручную — иначе matHit может «застрять».
    /// </summary>
    public void TakeDamage(int dmg, Unit attacker)
    {
        hp -= dmg;
        Debug.Log($"[Combat] {name} получил {dmg} урона (HP: {hp}/{maxHp})");

        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;

            RestoreAllOriginals();
        }

        flashCoroutine = StartCoroutine(Flash());

        if (hp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Визуальная вспышка «получил урон»: подмена материалов ВСЕХ рендереров
    /// кэша на matHit на 0.1 сек, затем возврат каждому его оригинала.
    /// </summary>
    private IEnumerator Flash()
    {
        if (matHit == null || rendererCache.Count == 0)
            yield break;

        foreach (var pair in rendererCache)
        {
            if (pair.renderer != null)
            {
                pair.renderer.material = matHit;
            }
        }

        yield return new WaitForSecondsRealtime(0.1f);

        if (gameObject != null)
        {
            RestoreAllOriginals();
        }

        flashCoroutine = null;
    }

    /// <summary>
    /// Пройти по кэшу и вернуть каждому рендереру его оригинальный материал.
    /// </summary>
    private void RestoreAllOriginals()
    {
        foreach (var pair in rendererCache)
        {
            if (pair.renderer != null && pair.original != null)
            {
                pair.renderer.material = pair.original;
            }
        }
    }

    /// <summary>
    /// Смерть юнита: остановить вспышку, освободить клетку, убрать себя
    /// из отряда, уничтожить объект.
    /// </summary>
    public void Die()
    {
        Debug.Log($"[Combat] {name} погиб");

        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }

        if (GridManager.Instance != null)
        {
            GridManager.Instance.FreeUnit(this);
        }

        if (Squad != null)
        {
            Squad.Remove(this);
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Запустить перемещение юнита из клетки fromCell в целевую клетку targetCell
    /// ПО ПУТИ A* (обход камней). Стартовая клетка передаётся ЯВНО.
    /// </summary>
    public void StartMove(Vector2Int fromCell, Vector2Int targetCell)
    {
        if (GridManager.Instance == null)
        {
            Debug.LogError("[Unit] StartMove: GridManager.Instance == null — движение отменено.", this);
            return;
        }

        List<Vector2Int> path = Pathfinding.AStar(fromCell, targetCell);

        if (path == null)
        {
            Debug.LogWarning($"[Unit] StartMove: путь недоступен ({fromCell} -> {targetCell}) — " +
                             $"юнит {name} остаётся на месте.", this);
            IsMoving = false;
            return;
        }

        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
        }

        lastGridPos = fromCell;

        moveCoroutine = StartCoroutine(MoveAlongPath(path, targetCell));
    }

    /// <summary>
    /// Корутина движения ПО КЛЕТКАМ пути: MoveTowards к центру каждой клетки
    /// пути со скоростью moveSpeed.
    /// </summary>
    private IEnumerator MoveAlongPath(List<Vector2Int> path, Vector2Int targetCell)
    {
        IsMoving = true;

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 waypointPos = GridManager.Instance.GridToWorld(path[i]);

            while (Vector3.Distance(transform.position, waypointPos) > 0.01f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    waypointPos,
                    moveSpeed * Time.deltaTime
                );

                yield return null;
            }
        }

        transform.position = GridManager.Instance.GridToWorld(targetCell);

        IsMoving = false;
        moveCoroutine = null;
    }

    private void OnDestroy()
    {
        if (GridManager.Instance != null)
        {
            GridManager.Instance.Free(GridPos, this);
        }
    }
}