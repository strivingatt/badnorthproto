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

    /// <summary>
    /// Флаг смерти (Fix-1, баг В): Destroy(gameObject) отложен до конца кадра,
    /// поэтому при двух ударах в одном боевом тике Die() мог сработать дважды
    /// (двойной лог смерти, повторный Squad.Remove). Все боевые входы
    /// (TakeDamage / TryAttack / Die) проверяют этот флаг.
    /// </summary>
    private bool isDead = false;

    /// <summary>Жив ли юнит (только чтение извне — для ИИ и боевых систем).</summary>
    public bool IsDead => isDead;

    /// <summary>
    /// Допуск при сравнении кулдауна с нулём: таймер уменьшается на tickInterval
    /// (float), поэтому после «восьми тиков по 0.1» в нём может остаться ~1e-8.
    /// Без допуска юнит изредка пропускал бы лишний тик.
    /// </summary>
    private const float CooldownEpsilon = 0.0001f;

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

    /// <summary>Авто-поворот только в бою: боевой радиус, клетки (Чебышёв).</summary>
    private const int CombatFacingRange = 6;

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
    /// Расстояние считаем в клетках метрикой Чебышёва max(|dx|,|dy|) (Fix-1, п.4:
    /// унификация меры с EnemyAI — «сосед по диагонали» = 1, а не 2).
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
        int bestDist = int.MaxValue;

        List<Unit> all = gm.GetAllUnits();

        for (int i = 0; i < all.Count; i++)
        {
            Unit u = all[i];

            // Мёртвый/уничтоженный юнит (unity-null или флаг isDead — Destroy
            // отложен до конца кадра) или своя команда — пропускаем.
            if (u == null || u.gameObject == null || u.isDead || u.team == this.team)
            {
                continue;
            }

            // Расстояние в клетках (Чебышёв, целочисленно). detectionRange
            // трактуется в клетках, а не в мировых единицах — не зависит от cellSize.
            int dx = Mathf.Abs(u.GridPos.x - GridPos.x);
            int dy = Mathf.Abs(u.GridPos.y - GridPos.y);
            int dist = Mathf.Max(dx, dy);

            if (dist <= detectionRange && dist < bestDist)
            {
                bestDist = dist;
                nearest = u;
            }
        }

        return nearest;
    }

    /// <summary>Боевой радиус авто-поворота, клеток (Чебышёв, Fix-2 п.2).</summary>
    public const int AutoFaceCombatRange = CombatFacingRange;

    /// <summary>
    /// Пересчитать направление взгляда (Fix-2, баг Б: перебивание facing'а
    /// формации). Авто-поворот включается ТОЛЬКО в бою:
    /// - юнит НЕ движется (IsMoving == false);
    /// - есть живой враг в боевом радиусе (Чебышёв от GridPos <= CombatFacingRange).
    /// Если условие не выполнено — Facing НЕ меняется (держится от последнего
    /// приказа CommandFormation или предыдущего врага), поэтому направление
    /// строя живёт дольше 0.2 с в покое.
    /// Facing врагов ведёт ТОЛЬКО EnemyAI — для team != Player early-return.
    /// Метрика унифицирована: везде Чебышёв max(|dx|,|dy|).
    /// </summary>
    public void UpdateFacing()
    {
        // Facing врагов ведёт только EnemyAI (Fix-2, п.2): авто-поворот игрока
        // не должен вмешиваться в поведение вражеских юнитов.
        if (team != Team.Player)
        {
            return;
        }

        // Во время движения facing не трогаем — он задан приказом формации.
        if (IsMoving)
        {
            return;
        }

        Unit enemy = FindNearestEnemyInCombatRange();

        if (enemy == null)
        {
            return; // врага в боевом радиусе нет — сохраняем текущий Facing
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
    /// Найти ближайшего живого врага в БОЕВОМ радиусе (Чебышёв <= CombatFacingRange, клетки).
    /// Метрика — Чебышёв max(|dx|,|dy|), та же, что у EnemyAI (Fix-2, п.2:
    /// унификация меры). Возвращает Unit или null.
    /// </summary>
    private Unit FindNearestEnemyInCombatRange()
    {
        var gm = GridManager.Instance;

        if (gm == null)
        {
            return null;
        }

        Unit nearest = null;
        int bestDist = int.MaxValue;

        List<Unit> all = gm.GetAllUnits();

        for (int i = 0; i < all.Count; i++)
        {
            Unit u = all[i];

            if (u == null || u.gameObject == null || u.isDead || u.team == this.team)
            {
                continue;
            }

            int dx = Mathf.Abs(u.GridPos.x - GridPos.x);
            int dy = Mathf.Abs(u.GridPos.y - GridPos.y);
            int dist = Mathf.Max(dx, dy); // Чебышёв

            if (dist <= CombatFacingRange && dist < bestDist)
            {
                bestDist = dist;
                nearest = u;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Синхронизация логической позиции с фактической (БЕЗ физики): каждый
    /// FixedUpdate читаем клетку под transform.position и, если юнит вошёл в
    /// новую клетку, обновляем GridPos и occupancy. Юниты не блокируют друг
    /// друга, поэтому в одной клетке может оказаться несколько юнитов —
    /// учёт хранит их списком (см. GridManager.occupancy).
    /// </summary>
    private void FixedUpdate()
    {
        var gm = GridManager.Instance;

        if (gm == null) return;

        Vector2Int currentCell = gm.WorldToGrid(transform.position);

        // Deadzone: не фиксируем смену клетки, пока юнит не подъехал
        // достаточно близко к её центру.
        Vector3 currentWorldPos = gm.GridToWorld(currentCell);
        float distToCenter = Vector3.Distance(transform.position, currentWorldPos);
        if (distToCenter > 0.4f) return;

        if (currentCell != GridPos && gm.InBounds(currentCell))
        {
            gm.Free(GridPos, this);
            GridPos = currentCell;
            gm.ForceOccupy(GridPos, this);
        }

        // Самовосстановление учёта: живой юнит обязан быть зарегистрирован
        // в своей клетке. Иначе он выпадает из GetAllUnits и становится
        // невидимым для боя и ИИ (например, если CommandFormation освободил
        // клетку, а путь к слоту не нашёлся).
        if (gm.InBounds(GridPos) && !gm.IsOccupiedBy(GridPos, this))
        {
            gm.ForceOccupy(GridPos, this);
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
    /// Таблица целочисленных поворотов направлений сетки на ±45° (Fix-1, баг А).
    /// Ключ — направление (8 допустимых векторов), значение — {ccw45, cw45}.
    /// Порядок пар согласован с Sectors (обход против часовой): ccw = следующий
    /// по массиву, cw = предыдущий.
    /// </summary>
    private static readonly Dictionary<Vector2Int, (Vector2Int ccw45, Vector2Int cw45)> Rot45 =
        new Dictionary<Vector2Int, (Vector2Int, Vector2Int)>
    {
        { new Vector2Int( 0,  1), (new Vector2Int(-1,  1), new Vector2Int( 1,  1)) }, // N  -> NW / NE
        { new Vector2Int( 1,  1), (new Vector2Int( 0,  1), new Vector2Int( 1,  0)) }, // NE -> N  / E
        { new Vector2Int( 1,  0), (new Vector2Int( 1,  1), new Vector2Int( 1, -1)) }, // E  -> NE / SE
        { new Vector2Int( 1, -1), (new Vector2Int( 1,  0), new Vector2Int( 0, -1)) }, // SE -> E  / S
        { new Vector2Int( 0, -1), (new Vector2Int( 1, -1), new Vector2Int(-1, -1)) }, // S  -> SE / SW
        { new Vector2Int(-1, -1), (new Vector2Int( 0, -1), new Vector2Int(-1,  0)) }, // SW -> S  / W
        { new Vector2Int(-1,  0), (new Vector2Int(-1, -1), new Vector2Int(-1,  1)) }, // W  -> SW / NW
        { new Vector2Int(-1,  1), (new Vector2Int(-1,  0), new Vector2Int( 0,  1)) }, // NW -> W  / N
    };

    /// <summary>
    /// Клетки зоны атаки (Fix-1, баг А): множество {F, rot45cw(F), rot45ccw(F)},
    /// где F = Facing — три СМЕЖНЫЕ клетки (фронт шириной 3 по 8-соседству).
    /// Реализовано целочисленной таблицей поворотов на 45°, а НЕ перпендикуляром
    /// right = (Facing.y, -Facing.x): для диагоналей перпендикуляр давал неверную
    /// зону (0,2),(1,1),(2,0) — удар через клетку без ортогональных соседей.
    /// Пример для Facing=(1,1): {(1,0), (1,1), (0,1)} + GridPos.
    /// Осевые направления дают ту же зону, что и прежняя логика.
    /// </summary>
    private List<Vector2Int> GetAttackCells()
    {
        List<Vector2Int> cells = new List<Vector2Int>(3);

        if (Rot45.TryGetValue(Facing, out var pair))
        {
            cells.Add(GridPos + Facing);      // центр фронта
            cells.Add(GridPos + pair.cw45);   // фланг по часовой
            cells.Add(GridPos + pair.ccw45);  // фланг против часовой
        }
        else
        {
            // Facing вне таблицы 8 направлений (защита): бьём хотя бы прямо.
            cells.Add(GridPos + Facing);
        }

        return cells;
    }

    /// <summary>
    /// Попытка атаки: цели — активные враги команды в зоне атаки.
    /// Кулдаун запускается только при наличии целей; при успешной атаке
    /// дёргается триггер анимации "Attack".
    ///
    /// Проверка кулдауна обязательна ЗДЕСЬ: CombatResolver вызывает TryAttack
    /// каждый боевой тик (0.1 с), поэтому без неё удар проходил 10 раз в секунду,
    /// а attackCooldown вообще ни на что не влиял.
    /// </summary>
    public bool TryAttack()
    {
        // Мёртвый юнит не атакует (Destroy отложен — объект жив до конца кадра).
        if (isDead) return false;

        var gm = GridManager.Instance;
        if (gm == null) return false;

        if (cooldownTimer > CooldownEpsilon) return false;

        List<Unit> targets = new List<Unit>();

        foreach (Vector2Int cell in GetAttackCells())
        {
            if (!gm.InBounds(cell)) continue;

            IReadOnlyList<Unit> occupants = gm.GetOccupants(cell);

            if (occupants == null) continue;

            // В клетке может стоять несколько юнитов («проход сквозь друг друга»),
            // поэтому перебираем всех: иначе второй юнит в клетке неуязвим.
            for (int i = 0; i < occupants.Count; i++)
            {
                Unit occupant = occupants[i];

                // Проверяем, что враг активен (not destroyed и не умер) — иначе
                // мёртвый юнит мог формально оставаться в клетке до конца кадра.
                if (occupant != null && occupant.team != this.team &&
                    occupant.gameObject != null && !occupant.isDead)
                {
                    targets.Add(occupant);
                }
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
        // Мёртвый юнит не получает урон (Fix-1, баг В): второй удар в том же
        // тике не должен повторно списывать HP и звать Die().
        if (isDead) return;

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
        // Защита от двойной смерти (Fix-1, баг В): Destroy(gameObject) отложен
        // до конца кадра, поэтому при двух ударах в одном тике Die() мог
        // вызваться дважды (двойной лог, повторный Remove из Squad).
        if (isDead) return;
        isDead = true;

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
    /// Запустить перемещение по УЖЕ РАССЧИТАННОМУ пути A* (Fix-2, баг А):
    /// путь построен в Squad.CommandFormation (проход 0) ДО освобождения
    /// клеток, поэтому повторный A* здесь не нужен и юнит гарантированно
    /// не остаётся «призраком» (клетка освобождена только для валидного пути).
    /// Стартовая клетка передаётся ЯВНО (для lastGridPos/страховки).
    /// </summary>
    public void StartMoveAlongPath(List<Vector2Int> path, Vector2Int fromCell, Vector2Int targetCell)
    {
        if (GridManager.Instance == null)
        {
            Debug.LogError("[Unit] StartMoveAlongPath: GridManager.Instance == null — движение отменено.", this);
            return;
        }

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning($"[Unit] StartMoveAlongPath: пустой путь ({fromCell} -> {targetCell}) — " +
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