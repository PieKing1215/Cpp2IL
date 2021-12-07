using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Model.CustomAttributes;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Utils;

public static class V29AttributeUtils
{
    public static Il2CppMethodDefinition[] ReadConstructors(Stream stream, uint count, ApplicationAnalysisContext context)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        var indices = new uint[count];

        for (var i = 0; i < count; i++) 
            indices[i] = reader.ReadUInt32();

        return indices.Select(i => context.Metadata.methodDefs[i]).ToArray();
    }

    public static AnalyzedCustomAttribute ReadAttribute(Stream stream, MethodAnalysisContext constructor, ApplicationAnalysisContext context)
    {
        var ret = new AnalyzedCustomAttribute(constructor);

        var numCtorArgs = stream.ReadUnityCompressedUint();
        var numFields = stream.ReadUnityCompressedUint();
        var numProps = stream.ReadUnityCompressedUint();
        
        if(numCtorArgs + numFields + numProps == 0)
            return ret;

        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        
        //Read constructor params
        for (var i = 0; i < numCtorArgs; i++) 
            ret.ConstructorParameters.Add(ReadBlob(reader, context));

        //Read fields
        for (var i = 0; i < numFields; i++)
        {
            var value = ReadBlob(reader, context);
            var fieldIndex = stream.ReadUnityCompressedInt();
            var field = ResolveMemberFromIndex(stream, constructor, context, fieldIndex, t => t.Fields);
            
            ret.Fields.Add(new(field, value));
        }

        //Read properties
        for (var i = 0; i < numProps; i++)
        {
            var value = ReadBlob(reader, context);
            var propIndex = stream.ReadUnityCompressedInt();
            var field = ResolveMemberFromIndex(stream, constructor, context, propIndex, t => t.Properties);
            
            ret.Properties.Add(new(field, value));
        }

        return ret;
    }

    private static T ResolveMemberFromIndex<T>(Stream stream, MethodAnalysisContext constructor, ApplicationAnalysisContext context, int memberIndex, Func<TypeAnalysisContext, List<T>> memberListGetter)
    {
        T member;
        if (memberIndex < 0)
        {
            //Member on a base type - get type index and clean up member index
            var typeIndex = stream.ReadUnityCompressedUint();
            memberIndex = -(memberIndex + 1);

            //Resolve type
            var typeDef = context.Metadata.typeDefs[typeIndex];
            var typeContext = context.ResolveContextForType(typeDef) ?? throw new("Unable to find type " + typeDef);

            //Get member
            member = memberListGetter(typeContext)[memberIndex];
        }
        else
            //Member on this type - simply get it.
            member = memberListGetter(constructor.DeclaringType!)[memberIndex];

        return member;
    }

    private static BaseCustomAttributeParameter ReadBlob(BinaryReader reader, ApplicationAnalysisContext context)
    {
        var ret = ReadTypeAndConstructParameter(reader, context);
        
        ret.ReadFromV29Blob(reader, context);

        return ret;
    }

    private static BaseCustomAttributeParameter ReadTypeAndConstructParameter(BinaryReader reader, ApplicationAnalysisContext context)
    {
        var rawTypeEnum = (Il2CppTypeEnum) reader.ReadByte();

        return ConstructParameterForType(reader, context, rawTypeEnum);
    }

    public static BaseCustomAttributeParameter ConstructParameterForType(BinaryReader reader, ApplicationAnalysisContext context, Il2CppTypeEnum rawTypeEnum)
    {
        switch (rawTypeEnum)
        {
            case Il2CppTypeEnum.IL2CPP_TYPE_ENUM:
                var enumTypeIndex = reader.BaseStream.ReadUnityCompressedInt();
                var enumType = context.Binary.GetType(enumTypeIndex);
                return new CustomAttributeEnumParameter(enumType, context);
            case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                return new CustomAttributeArrayParameter();
            case Il2CppTypeEnum.IL2CPP_TYPE_IL2CPP_TYPE_INDEX:
                return new CustomAttributeTypeParameter();
            case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
            case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
            case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                throw new("Object type not supported because libil2cpp is very vague");
            default:
                return new CustomAttributePrimitiveParameter(rawTypeEnum);
        }
    }
}