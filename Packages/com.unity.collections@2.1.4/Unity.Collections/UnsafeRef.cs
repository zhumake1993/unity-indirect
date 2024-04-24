using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;


namespace Unity.Collections
{
    /// <summary>
    /// An unmanaged single value.
    /// </summary>
    /// <remarks>The functional equivalent of an array of length 1.
    /// When you need just one value, UnsafeRef can be preferable to an array because it better conveys the intent.</remarks>
    /// <typeparam name="T">The type of value.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    //[NativeContainer]
    [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(int) })]
    public unsafe struct UnsafeRef<T>
        : INativeDisposable
        , IEquatable<UnsafeRef<T>>
        where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        internal void* m_Data;

        internal AllocatorManager.AllocatorHandle m_AllocatorLabel;

        /// <summary>
        /// Initializes and returns an instance of UnsafeRef.
        /// </summary>
        /// <param name="allocator">The allocator to use.</param>
        /// <param name="options">Whether newly allocated bytes should be zeroed out.</param>
        public UnsafeRef(AllocatorManager.AllocatorHandle allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            Allocate(allocator, out this);
            if (options == NativeArrayOptions.ClearMemory)
            {
                UnsafeUtility.MemClear(m_Data, UnsafeUtility.SizeOf<T>());
            }
        }

        /// <summary>
        /// Initializes and returns an instance of UnsafeRef.
        /// </summary>
        /// <param name="allocator">The allocator to use.</param>
        /// <param name="value">The initial value.</param>
        public UnsafeRef(T value, AllocatorManager.AllocatorHandle allocator)
        {
            Allocate(allocator, out this);
            *(T*)m_Data = value;
        }

        static void Allocate(AllocatorManager.AllocatorHandle allocator, out UnsafeRef<T> reference)
        {
            CollectionHelper.CheckAllocator(allocator);

            reference = default;
            reference.m_Data = Memory.Unmanaged.Allocate(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), allocator);
            reference.m_AllocatorLabel = allocator;
        }

        /// <summary>
        /// The value stored in this reference.
        /// </summary>
        /// <param name="value">The new value to store in this reference.</param>
        /// <value>The value stored in this reference.</value>
        public T Value
        {
            get
            {
                return *(T*)m_Data;
            }

            set
            {
                *(T*)m_Data = value;
            }
        }

        public ref T RefValue
        {
            get
            {
                return ref *(T*)m_Data;
            }
        }

        /// <summary>
        /// Whether this reference has been allocated (and not yet deallocated).
        /// </summary>
        /// <value>True if this reference has been allocated (and not yet deallocated).</value>
        public readonly bool IsCreated => m_Data != null;

        /// <summary>
        /// Releases all resources (memory and safety handles).
        /// </summary>
        public void Dispose()
        {
            if (!IsCreated)
            {
                return;
            }

            if (CollectionHelper.ShouldDeallocate(m_AllocatorLabel))
            {
                Memory.Unmanaged.Free(m_Data, m_AllocatorLabel);
                m_AllocatorLabel = Allocator.Invalid;
            }

            m_Data = null;
        }

        /// <summary>
        /// Creates and schedules a job that will release all resources (memory and safety handles) of this reference.
        /// </summary>
        /// <param name="inputDeps">A job handle. The newly scheduled job will depend upon this handle.</param>
        /// <returns>The handle of a new job that will release all resources (memory and safety handles) of this reference.</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (!IsCreated)
            {
                return inputDeps;
            }

            if (CollectionHelper.ShouldDeallocate(m_AllocatorLabel))
            {
                var jobHandle = new UnsafeRefDisposeJob
                {
                    Data = new UnsafeRefDispose
                    {
                        m_Data = m_Data,
                        m_AllocatorLabel = m_AllocatorLabel,
                    }
                }.Schedule(inputDeps);

                m_Data = null;
                m_AllocatorLabel = Allocator.Invalid;

                return jobHandle;
            }

            m_Data = null;

            return inputDeps;
        }

        /// <summary>
        /// Copy the value of another reference to this reference.
        /// </summary>
        /// <param name="reference">The reference to copy from.</param>
        public void CopyFrom(UnsafeRef<T> reference)
        {
            Copy(this, reference);
        }

        /// <summary>
        /// Copy the value of this reference to another reference.
        /// </summary>
        /// <param name="reference">The reference to copy to.</param>
        public void CopyTo(UnsafeRef<T> reference)
        {
            Copy(reference, this);
        }

        /// <summary>
        /// Returns true if the value stored in this reference is equal to the value stored in another reference.
        /// </summary>
        /// <param name="other">A reference to compare with.</param>
        /// <returns>True if the value stored in this reference is equal to the value stored in another reference.</returns>
        [ExcludeFromBurstCompatTesting("Equals boxes because Value does not implement IEquatable<T>")]
        public bool Equals(UnsafeRef<T> other)
        {
            return Value.Equals(other.Value);
        }

        /// <summary>
        /// Returns true if the value stored in this reference is equal to an object.
        /// </summary>
        /// <remarks>Can only be equal if the object is itself a UnsafeRef.</remarks>
        /// <param name="obj">An object to compare with.</param>
        /// <returns>True if the value stored in this reference is equal to the object.</returns>
        [ExcludeFromBurstCompatTesting("Takes managed object")]
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }
            return obj is UnsafeRef<T> && Equals((UnsafeRef<T>)obj);
        }

        /// <summary>
        /// Returns the hash code of this reference.
        /// </summary>
        /// <returns>The hash code of this reference.</returns>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }


        /// <summary>
        /// Returns true if the values stored in two references are equal.
        /// </summary>
        /// <param name="left">A reference.</param>
        /// <param name="right">Another reference.</param>
        /// <returns>True if the two values are equal.</returns>
        public static bool operator ==(UnsafeRef<T> left, UnsafeRef<T> right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Returns true if the values stored in two references are unequal.
        /// </summary>
        /// <param name="left">A reference.</param>
        /// <param name="right">Another reference.</param>
        /// <returns>True if the two values are unequal.</returns>
        public static bool operator !=(UnsafeRef<T> left, UnsafeRef<T> right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Copies the value of a reference to another reference.
        /// </summary>
        /// <param name="dst">The destination reference.</param>
        /// <param name="src">The source reference.</param>
        public static void Copy(UnsafeRef<T> dst, UnsafeRef<T> src)
        {
            UnsafeUtility.MemCpy(dst.m_Data, src.m_Data, UnsafeUtility.SizeOf<T>());
        }

        /// <summary>
        /// Returns a read-only reference aliasing the value of this reference.
        /// </summary>
        /// <returns>A read-only reference aliasing the value of this reference.</returns>
        public ReadOnly AsReadOnly()
        {
            return new ReadOnly(m_Data);
        }

        /// <summary>
        /// Returns a read-only native reference that aliases the content of a native reference.
        /// </summary>
        /// <param name="UnsafeRef">UnsafeRef to alias.</param>
        /// <returns>A read-only native reference that aliases the content of a native reference.</returns>
        public static implicit operator ReadOnly(UnsafeRef<T> UnsafeRef)
        {
            return UnsafeRef.AsReadOnly();
        }
        /// <summary>
        /// A read-only alias for the value of a UnsafeRef. Does not have its own allocated storage.
        /// </summary>
        [NativeContainer]
        [NativeContainerIsReadOnly]
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(int) })]
        public unsafe struct ReadOnly
        {
            [NativeDisableUnsafePtrRestriction]
            readonly void* m_Data;

            internal ReadOnly(void* data)
            {
                m_Data = data;
            }

            /// <summary>
            /// The value aliased by this reference.
            /// </summary>
            /// <value>The value aliased by the reference.</value>
            public T Value
            {
                get
                {
                    return *(T*)m_Data;
                }
            }
        }
    }

    //[NativeContainer]
    unsafe struct UnsafeRefDispose
    {
        [NativeDisableUnsafePtrRestriction]
        internal void* m_Data;

        internal AllocatorManager.AllocatorHandle m_AllocatorLabel;

        public void Dispose()
        {
            Memory.Unmanaged.Free(m_Data, m_AllocatorLabel);
        }
    }

    [BurstCompile]
    struct UnsafeRefDisposeJob : IJob
    {
        internal UnsafeRefDispose Data;

        public void Execute()
        {
            Data.Dispose();
        }
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    /// <summary>
    /// Provides extension methods for UnsafeRef.
    /// </summary>
    [GenerateTestsForBurstCompatibility]
    public static class UnsafeRefUnsafeUtility
    {
        /// <summary>
        /// Returns a pointer to this reference's stored value.
        /// </summary>
        /// <remarks>Performs a job safety check for read-write access.</remarks>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="reference">The reference.</param>
        /// <returns>A pointer to this reference's stored value.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(int) })]
        public static unsafe T* GetUnsafePtr<T>(this UnsafeRef<T> reference)
            where T : unmanaged
        {
            return (T*)reference.m_Data;
        }

        /// <summary>
        /// Returns a pointer to this reference's stored value.
        /// </summary>
        /// <remarks>Performs a job safety check for read-only access.</remarks>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="reference">The reference.</param>
        /// <returns>A pointer to this reference's stored value.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(int) })]
        public static unsafe T* GetUnsafeReadOnlyPtr<T>(this UnsafeRef<T> reference)
            where T : unmanaged
        {
            return (T*)reference.m_Data;
        }

        /// <summary>
        /// Returns a pointer to this reference's stored value.
        /// </summary>
        /// <remarks>Performs no job safety checks.</remarks>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="reference">The reference.</param>
        /// <returns>A pointer to this reference's stored value.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(int) })]
        public static unsafe T* GetUnsafePtrWithoutChecks<T>(this UnsafeRef<T> reference)
            where T : unmanaged
        {
            return (T*)reference.m_Data;
        }
    }
}
