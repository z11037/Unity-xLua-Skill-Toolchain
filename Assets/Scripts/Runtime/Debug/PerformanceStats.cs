public sealed class PerformanceStats
{
    public int ActiveRuntimeCount { get; private set; }

    public int ActiveBuffCount { get; private set; }

    public int TickBuffCount { get; private set; }

    public int TickExecuteCount { get; private set; }


    public void Reset()
    {
        ActiveRuntimeCount = 0;
        ActiveBuffCount = 0;
        TickBuffCount = 0;
        TickExecuteCount = 0;
    }


    public void AddRuntime()
    {
        ActiveRuntimeCount++;
    }


    public void AddBuff()
    {
        ActiveBuffCount++;
    }


    public void AddTickBuff()
    {
        TickBuffCount++;
    }


    public void AddTickExecute()
    {
        TickExecuteCount++;
    }
}