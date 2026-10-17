using UnityEngine;

/// <summary>
/// Система волн (аналог Bad North): после уничтожения всех врагов автоматически
/// запускается следующая волна. Сложность растёт ТОЛЬКО за счёт количества
/// врагов (без инфляции HP и урона, как в Bad North).
/// Между волнами — пауза с лечением игрока и обратным отсчётом в UI.
///
/// Первая волна запускается вручную клавишей E (InputController вызывает
/// StartNextWave) — игрок контролирует старт игры. Далее волны идут сами.
///
/// Синглтон — доступ через WaveManager.Instance.
/// </summary>
public class WaveManager : MonoBehaviour
{
    public static WaveManager Instance { get; private set; }

    [Header("Wave Settings")]
    [Tooltip("Количество врагов в первой волне.")]
    public int baseEnemyCount = 4;

    [Tooltip("Сколько врагов добавляется с каждой следующей волной.")]
    public int enemiesPerWave = 2;

    [Tooltip("Пауза между волнами, секунд.")]
    public float interWaveDelay = 5f;

    [Tooltip("Доля максимума HP, восстанавливаемая игроку между волнами (0.4 = 40% от 5 HP = 2 HP).")]
    public float healPercent = 0.4f;

    [Header("UI")]
    [Tooltip("Текст для отображения номера волны и таймера паузы " +
             "(UnityEngine.UI.Text или TextMeshProUGUI — оба поддерживаются).")]
    public ITextDisplay waveText;

    private int currentWave = 0;
    private bool isWaveActive = false;
    private float interWaveTimer = 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
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

    private void Update()
    {
        CheckWaveComplete();

        // Пауза между волнами: тикаем таймер, по достижении 0 — новая волна.
        if (!isWaveActive && interWaveTimer > 0f)
        {
            interWaveTimer -= Time.deltaTime;

            if (interWaveTimer <= 0f)
            {
                interWaveTimer = 0f;
                StartNextWave();
            }
            else
            {
                UpdatePauseText();
            }
        }
    }

    /// <summary>
    /// Запустить следующую волну: увеличить номер, вычислить количество врагов,
    /// позвать SquadSpawner.SpawnWave. Статы врагов НЕ масштабируются —
    /// сложность растёт только количеством.
    /// </summary>
    public void StartNextWave()
    {
        if (SquadSpawner.Instance == null)
        {
            Debug.LogError("[WaveManager] SquadSpawner.Instance == null — волна не запущена.");
            return;
        }

        currentWave++;

        // Количество растёт арифметически: 4, 6, 8, ... при enemiesPerWave = 2.
        int count = baseEnemyCount + (currentWave - 1) * enemiesPerWave;

        // Убрали множители, передаем только количество
        SquadSpawner.Instance.SpawnWave(count);

        isWaveActive = true;
        interWaveTimer = 0f;

        if (waveText != null)
        {
            waveText.SetText("Волна " + currentWave);
        }

        Debug.Log($"[WaveManager] Волна {currentWave}: врагов {count}.");
    }

    /// <summary>
    /// Проверка завершения волны: если волна активна и живых врагов не осталось —
    /// включить паузу, полечить игроков.
    /// </summary>
    private void CheckWaveComplete()
    {
        if (!isWaveActive) return;

        if (GridManager.Instance == null) return;

        var allUnits = GridManager.Instance.GetAllUnits();

        for (int i = 0; i < allUnits.Count; i++)
        {
            if (allUnits[i] != null && allUnits[i].team == Team.Enemy)
            {
                return; // хотя бы один враг жив — волна продолжается
            }
        }

        // Все враги уничтожены.
        isWaveActive = false;
        interWaveTimer = interWaveDelay;

        HealPlayerSquads();
        UpdatePauseText();

        Debug.Log($"[WaveManager] Волна {currentWave} завершена. " +
                  $"Пауза {interWaveDelay} сек, лечение игроков.");
    }

    /// <summary>
    /// Лечение всех живых юнитов игрока: healPercent от maxHp, минимум 1 HP.
    /// </summary>
    private void HealPlayerSquads()
    {
        if (GridManager.Instance == null) return;

        var allUnits = GridManager.Instance.GetAllUnits();

        for (int i = 0; i < allUnits.Count; i++)
        {
            var unit = allUnits[i];
            if (unit == null || unit.team != Team.Player) continue;

            int healAmount = Mathf.Max(1, (int)(unit.maxHp * healPercent));
            unit.Heal(healAmount);
            Debug.Log($"[Heal] {unit.name} вылечен на {healAmount} HP");
        }
    }

    /// <summary>
    /// Текст во время паузы: «Следующая волна через N сек».
    /// </summary>
    private void UpdatePauseText()
    {
        if (waveText == null) return;

        waveText.SetText("Следующая волна через " + Mathf.CeilToInt(interWaveTimer) + " сек");
    }
}
