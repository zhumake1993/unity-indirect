using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace ZGame.Indirect
{
    public unsafe struct IDGenerator
    {
        struct Data
        {
            public UnsafeList<int> IdStack;
            public int MaxID;
        }

        Data* _data;

        public void Init(int initialSize)
        {
            _data = MemoryUtility.Malloc<Data>(Allocator.Persistent);
            _data->IdStack = new UnsafeList<int>(initialSize, Allocator.Persistent);
            for (int i = initialSize - 1; i >= 0; i--)
            {
                _data->IdStack.Add(i);
            }

            _data->MaxID = initialSize - 1;
        }

        public void Dispose()
        {
            _data->IdStack.Dispose();
            MemoryUtility.Free(_data, Allocator.Persistent);
        }

        public int GetID()
        {
            if (_data->IdStack.Length == 0)
            {
                _data->MaxID++;
                return _data->MaxID;
            }

            int id = _data->IdStack[_data->IdStack.Length - 1];
            _data->IdStack.Length--;
            return id;
        }

        public void ReturnID(int id)
        {
            _data->IdStack.Add(id);
        }
    }
}