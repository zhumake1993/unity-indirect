using System;
using System.Threading;

namespace ZGame.Indirect
{
    public unsafe struct SimpleSpinLock
    {
        public struct AutoLock : IDisposable
        {
            SimpleSpinLock* _lock;

            public AutoLock(SimpleSpinLock* spinLock)
            {
                _lock = spinLock;
                _lock->Lock();
            }

            public void Dispose()
            {
                _lock->Unlock();
            }
        }

        public void Reset()
        {
            _lock = (int)LockState.Unlocked;
        }

        enum LockState
        {
            Unlocked = 0,
            Locked = 1,
        }

        int _lock;

        public void Lock()
        {
            while (Interlocked.CompareExchange(ref _lock, (int)LockState.Locked, (int)LockState.Unlocked) == (int)LockState.Locked) ;
        }

        public void Unlock()
        {
            Interlocked.Exchange(ref _lock, (int)LockState.Unlocked);
        }
    }
}