using System;

namespace Arshu.App.Flex
{
    public enum FlexType: byte
    {
        Null, Int, Uint, Float,
        Key, String, IndirectInt, IndirectUInt, IndirectFloat,
        Map, Vector, VectorInt, VectorUInt, VectorFloat, VectorKey, VectorString,
        VectorInt2, VectorUInt2, VectorFloat2,
        VectorInt3, VectorUInt3, VectorFloat3,
        VectorInt4, VectorUInt4, VectorFloat4,
        Blob, Bool, VectorBool = 36
    }

    public static class TypesUtil
    {
        public static bool IsInline(FlexType type)
        {
            return type == FlexType.Bool || (byte) type <= (byte)FlexType.Float;
        }

        public static bool IsTypedVectorElement(FlexType type)
        {
            var typeValue = (byte) type;
            return type == FlexType.Bool || (typeValue >= (byte)FlexType.Int && typeValue <= (byte)FlexType.String);
        }
        
        public static bool IsTypedVector(FlexType type)
        {
            var typeValue = (byte) type;
            return type == FlexType.VectorBool || (typeValue >= (byte)FlexType.VectorInt && typeValue <= (byte)FlexType.VectorString);
        }
        
        public static bool IsFixedTypedVector(FlexType type)
        {
            var typeValue = (byte) type;
            return (typeValue >= (byte)FlexType.VectorInt2 && typeValue <= (byte)FlexType.VectorFloat4);
        }

        public static bool IsAVector(FlexType type)
        {
            return IsTypedVector(type) || IsFixedTypedVector(type) || type == FlexType.Vector;
        }

        public static FlexType ToTypedVector(FlexType type, byte length)
        {
            var typeValue = (byte) type;
            if (length == 0)
            {
                return (FlexType) (typeValue - (byte)FlexType.Int + (byte)FlexType.VectorInt);
            }
            if (length == 2)
            {
                return (FlexType) (typeValue - (byte)FlexType.Int + (byte)FlexType.VectorInt2);
            }
            if (length == 3)
            {
                return (FlexType) (typeValue - (byte)FlexType.Int + (byte)FlexType.VectorInt3);
            }
            if (length == 4)
            {
                return (FlexType) (typeValue - (byte)FlexType.Int + (byte)FlexType.VectorInt4);
            }
            throw new Exception($"Unexpected length: {length}");
        }

        public static FlexType TypedVectorElementType(FlexType type)
        {
            var typeValue = (byte) type;
            return (FlexType) (typeValue - (byte)FlexType.VectorInt + (byte)FlexType.Int);
        }

        public static FlexType FixedTypedVectorElementType(FlexType type)
        {
            var fixedType = (byte) type - (byte)FlexType.VectorInt2;
            return (FlexType)(fixedType % 3 + (int)FlexType.Int);
        }
        
        public static int FixedTypedVectorElementSize(FlexType type)
        {
            var fixedType = (byte) type - (byte)FlexType.VectorInt2;
            return fixedType / 3 + 2;
        }

        public static byte PackedType(FlexType type, BitWidth bitWidth)
        {
            return (byte) ((byte) bitWidth | ((byte)type << 2));
        }

        public static byte NullPackedType()
        {
            return PackedType(FlexType.Null, BitWidth.Width8);
        }
    }
}