using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using XLua;

[LuaCallCSharp]
public class Character : MonoBehaviour
{
    [SerializeField] private Character testTarget;

    [FormerlySerializedAs("health")]
    [SerializeField] private FinalState initialMaxHealth;

    [FormerlySerializedAs("attack")]
    [SerializeField] private FinalState initialAttack;

    [SerializeField] private float baseMoveSpeed = 5f;
    [SerializeField] private CharacterMotor motor;

    [SerializeField] private List<SkillSO> skillConfigs = new List<SkillSO>();

    public string characterName;
    private CharacterRuntime runtime;
    private int characterId;

    private void Awake()
    {
        characterId = GetInstanceID();

        if (initialMaxHealth == null)
        {
            initialMaxHealth = new FinalState(100);
        }

        if (initialAttack == null)
        {
            initialAttack = new FinalState(10);
        }
    }

    private void Start()
    {
        if (CharacterRuntimeManager.Instance == null)
        {
            Debug.LogWarning($"½ÇÉ« {characterId} ×¢²áÊ§°Ü£ºCharacterRuntimeManager ÉÐÎ´³õÊ¼»¯");
            return;
        }

        runtime = CharacterRuntimeManager.Instance.RegisterCharacter(characterId, initialMaxHealth.Value, initialAttack.Value, baseMoveSpeed);

        if (runtime == null)
        {
            return;
        }

        if (motor != null)
        {
            motor.BindRuntime(runtime);
        }

        runtime.OnDied += HandleDied;

        if (SkillManager.Instance == null)
        {
            Log.Skill($"[Warning] ½ÇÉ« {characterId} ¼¼ÄÜ×¢²áÊ§°Ü£ºSkillManager ÉÐÎ´³õÊ¼»¯");
            return;
        }

        SkillManager.Instance.RegisterSkills(characterId, skillConfigs);
    }

    private void HandleDied()
    {
        Debug.Log($"½ÇÉ« {characterName} ÒÑËÀÍö");

        if (motor != null)
        {
            motor.Stop();
        }

        enabled = false;
    }

    public void TakeDamage(int damage)
    {
        CharacterRuntime runtime = GetRuntime();

        if (runtime == null)
        {
            return;
        }

        runtime.TakeDamage(damage);
    }

    public void Heal(int amount)
    {
        CharacterRuntime runtime = GetRuntime();

        if (runtime == null)
        {
            return;
        }

        runtime.Heal(amount);
    }

    public void AddAttack(int value)
    {
        CharacterRuntime runtime = GetRuntime();

        if (runtime == null)
        {
            return;
        }

        runtime.AddAttack(value);
    }

    public int GetAttackValue()
    {
        CharacterRuntime runtime = GetRuntime();

        if (runtime == null)
        {
            return Mathf.FloorToInt(initialAttack.Value);
        }

        return runtime.GetAttackValue();
    }

    public int GetHealthValue()
    {
        CharacterRuntime runtime = GetRuntime();

        if (runtime == null)
        {
            return Mathf.FloorToInt(initialMaxHealth.Value);
        }

        return runtime.GetHealthValue();
    }

    public int GetMaxHealthValue()
    {
        CharacterRuntime runtime = GetRuntime();

        if (runtime == null)
        {
            return Mathf.FloorToInt(initialMaxHealth.Value);
        }

        return runtime.GetMaxHealthValue();
    }

    public bool IsDead()
    {
        CharacterRuntime runtime = GetRuntime();
        return runtime != null && runtime.IsDead;
    }

    public CharacterRuntime GetRuntime()
    {
        if (runtime != null)
        {
            return runtime;
        }

        if (CharacterRuntimeManager.Instance == null)
        {
            return null;
        }

        return CharacterRuntimeManager.Instance.GetRuntime(characterId);
    }

    private void Update()
    {
        if (motor != null)
        {
            motor.Tick(Time.deltaTime);
        }
    }

    public void SetMoveDirection(Vector3 direction)
    {
        if (runtime == null || runtime.IsDead)
        {
            return;
        }

        if (motor == null)
        {
            return;
        }

        motor.SetMoveDirection(direction);
    }

    public void RequestTestSkill()
    {
        if (SkillManager.Instance == null)
        {
            return;
        }

        if (skillConfigs == null || skillConfigs.Count == 0)
        {
            return;
        }

        SkillSO skillConfig = skillConfigs[0];

        if (skillConfig == null || testTarget == null)
        {
            return;
        }

        SkillManager.Instance.RequestCast(characterId, skillConfig.skillID, this, testTarget);
    }

    private void OnDestroy()
    {
        if (runtime != null)
        {
            runtime.OnDied -= HandleDied;
            runtime = null;
        }

        if (CharacterRuntimeManager.Instance == null)
        {
            return;
        }

        CharacterRuntimeManager.Instance.UnregisterCharacter(characterId);
    }
}