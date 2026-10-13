using UnityEngine;

public class GameManager : MonoBehaviour
{
    private LuaManager luaManager;

    private void Awake()
    {
        luaManager = new LuaManager();
        luaManager.Init();

        Debug.Log("GameManager初始化完成");
    }

    private void Update()
    {
        if (luaManager != null)
        {
            luaManager.Tick();
        }
    }

    private void OnDestroy()
    {
        // 所有依赖 LuaEnv 的对象必须先释放。
        if (CharacterRuntimeManager.Instance != null)
        {
            CharacterRuntimeManager.Instance.DisposeAll();
        }

        // Skill 中的 LuaTable / delegate 都释放以后，
        // 才能安全销毁 LuaEnv。
        if (luaManager != null)
        {
            luaManager.Dispose();
            luaManager = null;
        }

        Debug.Log("GameManager已销毁");
    }
}