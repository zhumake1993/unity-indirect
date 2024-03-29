#define TEACK_MEMORY

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZGame.Indirect
{
    public static unsafe class MemoryUtility
    {
        public static T* Malloc<T>(Allocator allocator) where T : unmanaged
        {
#if TEACK_MEMORY
            return (T*)UnsafeUtility.MallocTracked(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), allocator, 0);
#else
            return (T*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), allocator);
#endif
        }

        public static T* Malloc<T>(int count, Allocator allocator) where T : unmanaged
        {
#if TEACK_MEMORY
            return (T*)UnsafeUtility.MallocTracked(UnsafeUtility.SizeOf<T>() * count, UnsafeUtility.AlignOf<T>(), allocator, 0);
#else
            return (T*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>() * count, UnsafeUtility.AlignOf<T>(), allocator);
#endif
        }

        public static void Free(void* ptr, Allocator allocator)
        {
#if TEACK_MEMORY
            UnsafeUtility.FreeTracked(ptr, allocator);
#else
            UnsafeUtility.Free(ptr, allocator);
#endif
        }

        public static T* MallocNoTrack<T>(Allocator allocator) where T : unmanaged
        {
            return (T*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), allocator);
        }

        public static T* MallocNoTrack<T>(int count, Allocator allocator) where T : unmanaged
        {
            return (T*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>() * count, UnsafeUtility.AlignOf<T>(), allocator);
        }

        public static void FreeNoTrack(void* ptr, Allocator allocator)
        {
            UnsafeUtility.Free(ptr, allocator);
        }
    }
}