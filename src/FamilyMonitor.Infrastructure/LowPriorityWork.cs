namespace FamilyMonitor.Infrastructure;

public static class LowPriorityWork
{
    public static T Run<T>(Func<T> work)
    {
        var thread=Thread.CurrentThread;var previous=thread.Priority;
        try{thread.Priority=ThreadPriority.BelowNormal;return work();}
        finally{thread.Priority=previous;}
    }
}
