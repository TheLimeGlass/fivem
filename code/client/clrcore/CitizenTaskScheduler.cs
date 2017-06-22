using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CitizenFX.Core
{
	class CitizenSynchronizationContext : SynchronizationContext
	{
		private static readonly List<Action> m_scheduledTasks = new List<Action>();

		public override void Post(SendOrPostCallback d, object state)
		{
			lock (m_scheduledTasks)
			{
				m_scheduledTasks.Add(() => d(state));
			}
		}

		public static void Tick()
		{
			Action[] tasks;

			lock (m_scheduledTasks)
			{
				tasks = m_scheduledTasks.ToArray();
				m_scheduledTasks.Clear();
			}

			foreach (var task in tasks)
			{
				try
				{
					task();
				}
				catch (Exception e)
				{
					Debug.WriteLine($"Exception during executing Post callback: {e}");
				}
			}
		}

		public override SynchronizationContext CreateCopy()
		{
			return this;
		}
	}

    class CitizenTaskScheduler : TaskScheduler
    {
        private readonly List<Task> m_runningTasks = new List<Task>();

        protected CitizenTaskScheduler()
        {
            
        }

        [SecurityCritical]
        protected override void QueueTask(Task task)
        {
            m_runningTasks.Add(task);
        }

        [SecurityCritical]
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (!taskWasPreviouslyQueued)
            {
                return TryExecuteTask(task);
            }

            return false;
        }

        [SecurityCritical]
        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return m_runningTasks;
        }

        public override int MaximumConcurrencyLevel => 1;

	    public void Tick()
        {
            var tasks = m_runningTasks.ToArray();

            foreach (var task in tasks)
            {
                InvokeTryExecuteTask(task);

                if (task.Exception != null)
                {
                    Debug.WriteLine("Exception thrown by a task: {0}", task.Exception.ToString());
                }

                if (task.IsCompleted || task.IsFaulted || task.IsCanceled)
                {
                    m_runningTasks.Remove(task);
                }
            }
        }

        [SecuritySafeCritical]
        private bool InvokeTryExecuteTask(Task task)
        {
            return TryExecuteTask(task);
        }

		[SecuritySafeCritical]
        public static void Create()
        {
            Instance = new CitizenTaskScheduler();

            Factory = new TaskFactory(Instance);

			TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

			var field = typeof(TaskScheduler).GetField("s_defaultTaskScheduler", BindingFlags.Static | BindingFlags.NonPublic);
			field.SetValue(null, Instance);

			field = typeof(Task).GetField("s_factory", BindingFlags.Static | BindingFlags.NonPublic);
			field.SetValue(null, Factory);
		}

		private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
		{
			Debug.WriteLine($"Unhandled task exception: {e.Exception.InnerExceptions.Aggregate("", (a, b) => $"{a}\n{b}")}");

			e.SetObserved();
		}

		public static TaskFactory Factory { get; private set; }

        public static CitizenTaskScheduler Instance { get; private set; }
    }
}
