using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UInt8 = System.Byte;

namespace ZGame.Indirect
{
    public static unsafe class BuddyUtility
    {
        public static bool IsPowerOfTwo(UInt32 value)
        {
            return (value & (value - 1)) == 0;
        }

        // same as tlsf_fls
        public static int FindLastBit(UInt32 value)
        {
            if (value == 0)
                return -1;

            int bit = 32;
            if ((value & 0xffff0000) == 0) { value <<= 16; bit -= 16; }
            if ((value & 0xff000000) == 0) { value <<= 8; bit -= 8; }
            if ((value & 0xf0000000) == 0) { value <<= 4; bit -= 4; }
            if ((value & 0xc0000000) == 0) { value <<= 2; bit -= 2; }
            if ((value & 0x80000000) == 0) { bit -= 1; }

            return bit - 1;
        }

        public static int FindFirstBit(UInt32 value)
        {
            if (value == 0)
                return -1;

            int bit = 1;
            if ((value & 0x0000ffff) == 0) { value >>= 16; bit += 16; }
            if ((value & 0x000000ff) == 0) { value >>= 8; bit += 8; }
            if ((value & 0x0000000f) == 0) { value >>= 4; bit += 4; }
            if ((value & 0x00000003) == 0) { value >>= 2; bit += 2; }
            if ((value & 0x00000001) == 0) { bit += 1; }

            return bit - 1;
        }

        public static int FindFirstBit(UInt64 value)
        {
            UInt32 low = (UInt32)(value & 0xffffffff);
            UInt32 high = (UInt32)(value >> 32);

            if (low != 0)
                return FindFirstBit(low);
            else if (high != 0)
                return FindFirstBit(high) + 32;
            else
                return -1;
        }

        public static string BuddyAllocatorStatsToString(BuddyAllocatorStats stats)
        {
            string str = "";

            str += $"MinSize={stats.MinSize}\n";
            str += $"MaxSize={stats.MaxSize}\n";
            str += $"NumMaxSize={stats.NumMaxSize}\n";

            str += $"NumLevels={stats.NumLevels}\n";
            for (int i = (int)stats.NumLevels - 1; i >= 0; --i)
            {
                str += $"\tL{i}({stats.Levels[i].BlockSize}): {stats.Levels[i].NumFreeBlocks}/{stats.Levels[i].NumBlock}\n";
            }

            str += $"TotalBytes={stats.TotalBytes}\n";
            str += $"AllocatedBytes={stats.AllocatedBytes} ({stats.AllocatedBytes * 1.0f / stats.TotalBytes * 100}%)\n";

            return str;
        }
    }

    public struct Chunk : IEquatable<Chunk>
    {
        public const UInt32 c_AddressBits = 32;
        public static readonly Chunk s_InvalidChunk = new Chunk() { Value = UInt64.MaxValue };
        public UInt64 Value;

        public UInt8 IndexOf()
        {
            return (UInt8)(Value >> (int)Chunk.c_AddressBits);
        }

        // Byte
        public UInt32 AddressOf()
        {
            return (UInt32)(Value & (((UInt64)1 << (int)c_AddressBits) - 1));
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is Chunk)
            {
                return Equals((Chunk)obj);
            }

            return false;
        }

        public bool Equals(Chunk other)
        {
            return Value == other.Value;
        }

        public static bool operator ==(Chunk a, Chunk b)
        {
            return a.Value == b.Value;
        }

        public static bool operator !=(Chunk a, Chunk b)
        {
            return a.Value != b.Value;
        }
    }

    public unsafe struct FreeBlockRegistryLevel
    {
        public UInt64* Bits; // 1 means free
        public int NumSetBits; // number of free memory
        public UInt32 NumFields;
        public int FieldHint; // the last freed field
        public UInt32 NumBits;

        public void Init(UInt32 numBits)
        {
            Utility.Assert(numBits > 0);
            NumBits = numBits;

            UInt32 bitsPerField = sizeof(UInt64) * 8;
            NumFields = (numBits + bitsPerField - 1) / bitsPerField;
            Utility.Assert(NumFields > 0);
            Utility.Assert(NumFields <= int.MaxValue);

            Bits = MemoryUtility.Malloc<UInt64>((int)NumFields, Allocator.Persistent);
            UnsafeUtility.MemSet(Bits, 0, sizeof(UInt64) * (int)NumFields);

            NumSetBits = 0;
            FieldHint = 0;
        }

        public void Dispose()
        {
            MemoryUtility.Free(Bits, Allocator.Persistent);
        }
    }

    public unsafe struct BuddyAllocatorData
    {
        public UInt32 MinSize;
        public UInt32 MaxSize;
        public UInt32 NumMaxSize;
        public UInt8 MinSizeShift;
        public UInt8 MaxSizeShift;
        public UInt8 MaxIndex;
        public UInt8 NumLevels;
        public FreeBlockRegistryLevel* Levels;
        public SimpleSpinLock* Lock;
    }

    public struct LevelStats
    {
        public UInt32 NumBlock;
        public UInt32 NumFreeBlocks;
        public UInt32 BlockSize;
    }

    public struct BuddyAllocatorStats
    {
        public UInt32 MinSize;
        public UInt32 MaxSize;
        public UInt32 NumMaxSize;

        public UInt32 NumLevels;
        public NativeArray<LevelStats> Levels; // tmp memory, the caller is responsible to release it

        public UInt64 TotalBytes;
        public UInt64 AllocatedBytes;

        public void Dispose()
        {
            Levels.Dispose();
        }
    }

    public unsafe partial struct BuddyAllocator
    {
        BuddyAllocatorData* _data;

        public void Init(UInt32 minSizeBytes, UInt32 maxSizeBytes, UInt32 numMaxSizeBlocks)
        {
            _data = MemoryUtility.Malloc<BuddyAllocatorData>(Allocator.Persistent);

            Utility.Assert(minSizeBytes > 0);
            Utility.Assert(BuddyUtility.IsPowerOfTwo(minSizeBytes));
            Utility.Assert(maxSizeBytes >= minSizeBytes);
            Utility.Assert(BuddyUtility.IsPowerOfTwo(maxSizeBytes));
            Utility.Assert(numMaxSizeBlocks > 0);
            Utility.Assert(BuddyUtility.IsPowerOfTwo(numMaxSizeBlocks)); // really needed?

            _data->MinSize = minSizeBytes;
            _data->MaxSize = maxSizeBytes;
            _data->NumMaxSize = numMaxSizeBlocks;

            _data->MinSizeShift = (UInt8)BuddyUtility.FindLastBit(minSizeBytes);
            _data->MaxSizeShift = (UInt8)BuddyUtility.FindLastBit(maxSizeBytes);
            _data->MaxIndex = (UInt8)(_data->MaxSizeShift - _data->MinSizeShift);

            UInt64 virtualMaxSize = (UInt64)_data->MaxSize * (UInt64)_data->NumMaxSize;
            UInt64 maxElementsAtIndex0 = virtualMaxSize / _data->MinSize;
            Utility.Assert(maxElementsAtIndex0 <= UInt32.MaxValue);

            _data->NumLevels = (UInt8)(_data->MaxIndex + 1);
            _data->Levels = MemoryUtility.Malloc<FreeBlockRegistryLevel>(_data->NumLevels, Allocator.Persistent);

            for (int i = 0; i < _data->NumLevels; ++i)
            {
                _data->Levels[i].Init((UInt32)(maxElementsAtIndex0 >> i));
            }

            for (int i = 0; i < _data->NumMaxSize; ++i)
            {
                MarkFree(ref _data->Levels[_data->MaxIndex], (UInt32)i);
            }

            _data->Lock = MemoryUtility.Malloc<SimpleSpinLock>(Allocator.Persistent);
            _data->Lock->Reset();

            Utility.Assert(IsEmpty());
        }

        public void Dispose()
        {
            for (int i = 0; i < _data->NumLevels; ++i)
                _data->Levels[i].Dispose();

            MemoryUtility.Free(_data->Levels, Allocator.Persistent);
            MemoryUtility.Free(_data->Lock, Allocator.Persistent);
            MemoryUtility.Free(_data, Allocator.Persistent);
        }

        public bool IsEmpty()
        {
            return CountFree(ref _data->Levels[_data->MaxIndex]) == _data->NumMaxSize;
        }

        public Chunk Alloc(UInt32 size)
        {
            using (new SimpleSpinLock.AutoLock(_data->Lock))
            {
                UInt8 index = (size <= _data->MinSize) ? (UInt8)0 : (UInt8)(BuddyUtility.FindLastBit(size - 1) + 1 - _data->MinSizeShift);

                UInt32 addr = 0;
                if (!InternalAllocate(index, ref addr))
                    return Chunk.s_InvalidChunk;

                return MakeChunk(addr << (_data->MinSizeShift + index), index);
            }
        }

        bool InternalAllocate(UInt8 index, ref UInt32 addr)
        {
            if (index > _data->MaxIndex)
                return false; // request too large

            if (!TakeAny(ref _data->Levels[index], ref addr))
            {
                // current level has no free memory, try to split the higher level
                UInt32 addrToSplit = 0;
                if (!InternalAllocate((UInt8)(index + 1), ref addrToSplit))
                    return false; // out of memory

                addrToSplit <<= 1;
                // one is left from split
                MarkFree(ref _data->Levels[index], addrToSplit + 1);
                addr = addrToSplit;
            }

            return true;
        }

        public void Free(Chunk chunk)
        {
            using (new SimpleSpinLock.AutoLock(_data->Lock))
            {
                UInt8 index = chunk.IndexOf();
                UInt32 addr = chunk.AddressOf() >> (_data->MinSizeShift + index);

                InternalFree(addr, index);
            }
        }

        void InternalFree(UInt32 addr, UInt8 index)
        {
            if (index == _data->MaxIndex)
            {
                MarkFree(ref _data->Levels[index], addr);
            }
            else
            {
                if (MarkFreeOrMerge(ref _data->Levels[index], addr, BuddyAddr(addr)))
                    InternalFree(addr >> 1, (UInt8)(index + 1));
            }
        }

        public UInt32 SizeOf(Chunk chunk)
        {
            UInt8 index = chunk.IndexOf();
            return SizeFromIndex(index, _data->MinSizeShift);
        }

        public UInt32 BlockIndex(Chunk chunk)
        {
            return chunk.AddressOf() >> _data->MaxSizeShift;
        }

        public UInt32 BlockOffset(Chunk chunk)
        {
            return chunk.AddressOf() & (_data->MaxSize - 1);
        }

        public BuddyAllocatorStats GetStats()
        {
            BuddyAllocatorStats stats = new BuddyAllocatorStats();

            stats.MinSize = _data->MinSize;
            stats.MaxSize = _data->MaxSize;
            stats.NumMaxSize = _data->NumMaxSize;

            stats.NumLevels = _data->NumLevels;
            stats.Levels = new NativeArray<LevelStats>((int)stats.NumLevels, Allocator.Temp);

            UInt64 virtualMaxSize = (UInt64)_data->MaxSize * (UInt64)_data->NumMaxSize;
            UInt64 maxElementsAtIndex0 = virtualMaxSize / _data->MinSize;

            for (int i = 0; i < stats.NumLevels; ++i)
            {
                LevelStats levelStats = new LevelStats();
                levelStats.NumBlock = (UInt32)(maxElementsAtIndex0 >> i);
                levelStats.NumFreeBlocks = CountFree(ref _data->Levels[i]);
                levelStats.BlockSize = SizeFromIndex((UInt8)i, _data->MinSizeShift);
                stats.Levels[i] = levelStats;
            }

            stats.TotalBytes = virtualMaxSize;

            UInt64 freeBytes = 0;
            for (int i = 0; i < stats.NumLevels; ++i)
            {
                freeBytes += (UInt64)stats.Levels[i].NumFreeBlocks * (UInt64)SizeFromIndex((UInt8)i, _data->MinSizeShift);
            }

            stats.AllocatedBytes = stats.TotalBytes - freeBytes;

            return stats;
        }
    }

    public unsafe partial struct BuddyAllocator
    {
        Chunk MakeChunk(UInt32 address, UInt8 index)
        {
            Chunk c;
            c.Value = ((UInt64)index << (int)Chunk.c_AddressBits) | (UInt64)(address);

            return c;
        }

        UInt32 SizeFromIndex(UInt8 index, UInt32 shift)
        {
            return (UInt32)(1 << (int)(index + shift));
        }

        UInt32 FieldIndex(UInt32 bitAddr)
        {
            return bitAddr / (sizeof(UInt64) * 8);
        }

        UInt64 FieldBitIndex(UInt64 bitAddr)
        {
            return bitAddr & ((sizeof(UInt64) * 8) - 1);
        }

        UInt32 BuddyAddr(UInt32 addr)
        {
            return (UInt32)(((addr & 1) != 0) ? (addr & (~1)) : (addr | 1));
        }

        void MarkFree(ref FreeBlockRegistryLevel freeAddrs, UInt32 addr)
        {
            UInt32 fieldIndex = FieldIndex(addr);
            UInt64 bitIndex = FieldBitIndex(addr);

            freeAddrs.Bits[fieldIndex] |= (UInt64)1 << (int)bitIndex;
            freeAddrs.NumSetBits++;
            freeAddrs.FieldHint = (int)fieldIndex;
        }

        bool MarkFreeOrMerge(ref FreeBlockRegistryLevel freeAddrs, UInt32 addr, UInt32 mergeableAddr)
        {
            UInt32 fieldIndex = FieldIndex(addr);
            //Utility.Assert(fieldIndex == FieldIndex(mergeableAddr)); // avoid this check to speed up

            UInt64 maskAddr = (UInt64)1 << (int)FieldBitIndex(addr);
            UInt64 maskOther = (UInt64)1 << (int)FieldBitIndex(mergeableAddr);

            ref UInt64 field = ref freeAddrs.Bits[fieldIndex];

            bool result;
            if ((field & maskOther) != 0)
            {
                field &= ~maskOther;
                result = true;
            }
            else
            {
                field |= maskAddr;
                result = false;
            }

            freeAddrs.NumSetBits += result ? -1 : 1;

            if (result)
                freeAddrs.FieldHint = (int)fieldIndex;

            return result;
        }

        bool TakeAnyFromField(ref UInt64 field, ref UInt32 bitInField)
        {
            if (field == 0)
                return false;

            int b = BuddyUtility.FindFirstBit(field);
            if (b < 0)
                return false;

            field = field & (~((UInt64)1 << b));
            bitInField = (UInt32)b;

            return true;
        }

        bool TakeAny(ref FreeBlockRegistryLevel freeAddrs, ref UInt32 addr)
        {
            if (freeAddrs.NumSetBits == 0)
                return false; // none left

            UInt32 bitInField = 0;

            UInt32 hint = (UInt32)freeAddrs.FieldHint;
            if (TakeAnyFromField(ref freeAddrs.Bits[hint], ref bitInField))
            {
                addr = (hint * sizeof(UInt64) * 8) + bitInField;
                freeAddrs.NumSetBits--;
                return true;
            }

            for (UInt32 i = 0; i < freeAddrs.NumFields; ++i)
            {
                if (TakeAnyFromField(ref freeAddrs.Bits[i], ref bitInField))
                {
                    addr = (i * sizeof(UInt64) * 8) + bitInField;
                    freeAddrs.NumSetBits--;
                    return true;
                }
            }

            return false;
        }

        UInt32 CountFree(ref FreeBlockRegistryLevel freeAddrs)
        {
            return (UInt32)freeAddrs.NumSetBits;
        }
    }
}