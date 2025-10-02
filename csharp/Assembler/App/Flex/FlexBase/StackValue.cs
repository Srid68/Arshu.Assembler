using System;
using System.Runtime.InteropServices;

namespace Arshu.App.Flex
{
    [StructLayout(LayoutKind.Explicit, Size=10)]
    public struct StackValue
    {
        [FieldOffset(0)] private ulong UValue;
        
        [FieldOffset(0)] private long LValue;
        
        [FieldOffset(0)] private double DValue;

        [FieldOffset(8)] private BitWidth Width;

        [FieldOffset(9)] private FlexType ValueType;

        public static StackValue Null()
        {
            return new StackValue
            {
                Width = BitWidth.Width8,
                LValue = 0,
                ValueType = FlexType.Null
            };
        }
        
        public static StackValue Value(float value)
        {
            return new StackValue
            {
                Width = BitWidth.Width32,
                DValue = value,
                ValueType = FlexType.Float
            };
        }
        
        public static StackValue Value(double value)
        {
            return new StackValue
            {
                Width = BitWidthUtil.Width(value),
                DValue = value,
                ValueType = FlexType.Float
            };
        }
        
        public static StackValue Value(bool value)
        {
            return new StackValue
            {
                Width = BitWidth.Width8,
                LValue = value ? 1 : 0,
                ValueType = FlexType.Bool
            };
        }
        
        public static StackValue Value(long value)
        {
            return new StackValue
            {
                Width = BitWidthUtil.Width(value),
                LValue = value,
                ValueType = FlexType.Int
            };
        }
        
        public static StackValue Value(ulong value)
        {
            return new StackValue
            {
                Width = BitWidthUtil.Width(value),
                UValue = value,
                ValueType = FlexType.Uint
            };
        }
        
        public static StackValue Value(ulong value, BitWidth width, FlexType type)
        {
            return new StackValue
            {
                Width = width,
                UValue = value,
                ValueType = type
            };
        }
        
        public static StackValue Value(long value, BitWidth width, FlexType type)
        {
            return new StackValue
            {
                Width = width,
                LValue = value,
                ValueType = type
            };
        }

        public BitWidth StoredWidth(BitWidth bitWidth = BitWidth.Width8)
        {
            if (TypesUtil.IsInline(ValueType))
            {
                return (BitWidth) Math.Max((int) bitWidth, (int) Width);
            }

            return Width;
        }

        public byte StoredPackedType(BitWidth bitWidth = BitWidth.Width8)
        {
            return TypesUtil.PackedType(ValueType, StoredWidth(bitWidth));
        }

        public BitWidth ElementWidth(ulong size, int index)
        {
            if (TypesUtil.IsInline(ValueType))
            {
                return Width;
            }

            for (var i = 0; i < 4; i++)
            {
                var width = (ulong)1 << i;
                var offsetLoc = size + BitWidthUtil.PaddingSize(size, width) + (ulong)index * width;
                var offset = offsetLoc - UValue;
                var bitWidth = BitWidthUtil.Width(offset);
                if ((1UL << (byte) bitWidth) == width)
                {
                    return bitWidth;
                }
            }
            throw new Exception($"Element with size: {size} and index: {index} is of unknown width");
        }

        public long AsLong => LValue;
        public ulong AsULong => UValue;
        public double AsDouble => DValue;

        public bool IsFloat32 => ValueType == FlexType.Float && Width == BitWidth.Width32;
        public bool IsOffset => TypesUtil.IsInline(ValueType) == false;

        public FlexType TypeOfValue => ValueType;
        public BitWidth InternalWidth => Width;
    }
}