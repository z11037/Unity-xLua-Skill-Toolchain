using System.Collections.Generic;
using UnityEngine;

public sealed class CharacterRuntimeManager : MonoBehaviour
{
    public static CharacterRuntimeManager Instance { get; private set; }

    private readonly Dictionary<int, CharacterRuntime> runtimes = new Dictionary<int, CharacterRuntime>();

    private bool isDisposed;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("检测到重复的 CharacterRuntimeManager，销毁当前对象");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        Debug.Log("CharacterRuntimeManager 初始化完成");
    }

    public CharacterRuntime RegisterCharacter(int characterId, float initialMaxHealth, float initialAttack, float initialMoveSpeed)
    {
        if (runtimes.TryGetValue(characterId, out CharacterRuntime oldRuntime))
        {
            Debug.LogWarning($"角色 {characterId} 已注册，将替换旧 Runtime");

            oldRuntime.Dispose();
            runtimes.Remove(characterId);
        }

        CharacterRuntime runtime = new CharacterRuntime(characterId, initialMaxHealth, initialAttack, initialMoveSpeed);
        runtimes.Add(characterId, runtime);

        Debug.Log($"角色 {characterId} Runtime 注册完成");

        return runtime;
    }

    public bool UnregisterCharacter(int characterId)
    {
        if (!runtimes.TryGetValue(characterId, out CharacterRuntime runtime))
        {
            return false;
        }

        runtime.Dispose();
        runtimes.Remove(characterId);

        Debug.Log($"角色 {characterId} Runtime 已注销");

        return true;
    }

    public bool TryGetRuntime(int characterId, out CharacterRuntime runtime)
    {
        return runtimes.TryGetValue(characterId, out runtime);
    }

    public CharacterRuntime GetRuntime(int characterId)
    {
        runtimes.TryGetValue(characterId, out CharacterRuntime runtime);
        return runtime;
    }

    public IEnumerable<CharacterRuntime> GetAllRuntimes()
    {
        return runtimes.Values;
    }

    public void DisposeAll()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        foreach (CharacterRuntime runtime in runtimes.Values)
        {
            runtime.Dispose();
        }

        runtimes.Clear();

        Debug.Log("CharacterRuntimeManager 已释放全部 Runtime");
    }

    private void OnDestroy()
    {
        DisposeAll();

        if (Instance == this)
        {
            Instance = null;
        }

        Debug.Log("CharacterRuntimeManager 已销毁");
    }
}