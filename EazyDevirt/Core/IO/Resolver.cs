﻿using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures.Types.Parsing;
using EazyDevirt.Architecture;
using EazyDevirt.Architecture.InlineOperands;
using EazyDevirt.Devirtualization;

namespace EazyDevirt.Core.IO;

internal class Resolver
{
    public Resolver(DevirtualizationContext ctx)
    {
        Ctx = ctx;
        VMStreamReader = new VMBinaryReader(new CryptoStreamV3(Ctx.VMResolverStream, Ctx.MethodCryptoKey, true));
    }
    
    private DevirtualizationContext Ctx { get; }
    
    private VMBinaryReader VMStreamReader { get; }
    
    public ITypeDefOrRef ResolveType(int position)
    {
        Ctx.VMResolverStream.Seek(position, SeekOrigin.Begin);
        
        var inlineOperand = new VMInlineOperand(VMStreamReader);
        if (inlineOperand.IsToken)
            return (TypeDefinition)Ctx.Module.LookupMember(inlineOperand.Token);
        
        if (!inlineOperand.HasData || inlineOperand.Data is not VMTypeData data)
            throw new Exception("VM inline operand expected to have type data!");
        
        var typeDefOrRef = TypeNameParser.Parse(Ctx.Module, data.Name).ToTypeDefOrRef();

        var assembly = typeDefOrRef.Scope?.GetAssembly()!;
        var assemblyResolver = Ctx.Module.MetadataResolver.AssemblyResolver;
        if (!assemblyResolver.HasCached(assembly))
        {
            var assemblyDefinition = assemblyResolver.Resolve(assembly);
            if (assemblyDefinition == null)
            {
                Ctx.Console.Error("Failed resolving assembly " + assembly.FullName);
                return null!;
            }
        }
        
        var resolvedTypeDef = typeDefOrRef.Resolve() ?? typeDefOrRef;
        
        if (!data.HasGenericTypes)
            return resolvedTypeDef;
        
        var generics = data.GenericTypes.Select(g => ResolveType(g.Position)).Select(gtype => gtype.ToTypeSignature()).ToArray();
        return resolvedTypeDef.MakeGenericInstanceType(generics).ToTypeDefOrRef();
    }
    
    public FieldDefinition ResolveField(int position)
    {
        Ctx.VMResolverStream.Seek(position, SeekOrigin.Begin);
        
        var inlineOperand = new VMInlineOperand(VMStreamReader);
        if (inlineOperand.IsToken)
            return (FieldDefinition)Ctx.Module.LookupMember(inlineOperand.Token);
        
        if (!inlineOperand.HasData || inlineOperand.Data is not VMFieldData data)
            throw new Exception("VM inline operand expected to have field data!");
        
        var declaringType = ResolveType(data.FieldType.Position);
        if (declaringType == null)
            throw new Exception("Unable to resolve vm field type as TypeDef or TypeRef!");
        
        if (declaringType is TypeDefinition definition)
            return definition.Fields.First(f => f.Name == data.Name);
        
        // TODO: verify if this ever happens, and fix it if it does
        if (declaringType is TypeReference reference)
            throw new Exception("VM field declaring type is a TypeRef!");
        // return ((TypeDefinition)Ctx.Module.LookupMember(reference.MetadataToken)).Fields.First(f => f.Name == data.Name);
        
        throw new Exception("VM field declaringType neither TypeDef nor TypeRef!");
    }
    
    public MethodDefinition ResolveMethod(int position)
    {
        Ctx.VMResolverStream.Seek(position, SeekOrigin.Begin);
        
        var inlineOperand = new VMInlineOperand(VMStreamReader);
        if (inlineOperand.IsToken)
            return (MethodDefinition)Ctx.Module.LookupMember(inlineOperand.Token);
        
        if (!inlineOperand.HasData || inlineOperand.Data is not VMMethodData data)
            throw new Exception("VM inline operand expected to have method data!");
        
        var declaringType = ResolveType(data.DeclaringType.Position);
        if (declaringType == null)
            throw new Exception("Unable to resolve vm method declaring type");

        // this most likely means it is a generic type
        if (declaringType is TypeSpecification spec)
            declaringType = declaringType.Resolve() ?? declaringType;
        
        if (declaringType is TypeDefinition definition)
            return definition.Methods.First(f => f.Name == data.Name);
        
        // TODO: verify if this ever happens, and fix it if it does
        if (declaringType is TypeReference reference)
            throw new Exception("VM method declaring type is a TypeRef!");

        throw new Exception("VM method declaring type neither TypeDef nor TypeRef!");
    }

    public IMetadataMember ResolveToken(int position)
    {
        Ctx.VMResolverStream.Seek(position, SeekOrigin.Begin);
        
        var inlineOperand = new VMInlineOperand(VMStreamReader);
        if (inlineOperand.IsToken)
            return Ctx.Module.LookupMember(inlineOperand.Token);

        if (!inlineOperand.HasData)
            throw new Exception("VM inline operand expected to have data!");

        return inlineOperand.Data.Type switch
        {
            VMInlineOperandType.Type => ResolveType(position),
            VMInlineOperandType.Field => ResolveField(position),
            VMInlineOperandType.Method => ResolveMethod(position),
            _ => throw new ArgumentOutOfRangeException(nameof(inlineOperand.Data.Type), inlineOperand.Data.Type, "VM inline operand data is neither Type, Field, nor Method!")
        };
    }
    
    public MethodDefinition ResolveEazCall(int value)
    {
        // var noGenericVars = (value & 0x80000000) != 0; 
        // var forceResolveGenericVars = (value & 0x40000000) != 0;
        var position = value & 0x3FFFFFFF;

        Ctx.VMResolverStream.Seek(position, SeekOrigin.Begin);
        
        var methodInfo = new VMMethodInfo(VMStreamReader);
        
        var declaringType = ResolveType(methodInfo.VMDeclaringType);
        if (declaringType == null)
            throw new Exception("Unable to resolve vm eaz call declaring type");
        
        if (declaringType is TypeDefinition definition)
            return definition.Methods.First(f => f.Name == methodInfo.Name);
        
        // TODO: verify if this ever happens, and fix it if it does
        if (declaringType is TypeReference reference)
            throw new Exception("VM eaz call declaring type is a TypeRef!");
        
        throw new Exception("VM eaz call method declaring type neither TypeDef nor TypeRef!");
    }

    public string ResolveString(int position)
    {
        Ctx.VMResolverStream.Seek(position, SeekOrigin.Begin);
        
        var inlineOperand = new VMInlineOperand(VMStreamReader);
        if (inlineOperand.IsToken)
            return Ctx.Module.LookupString(inlineOperand.Token);
        
        if (!inlineOperand.HasData || inlineOperand.Data is not VMStringData data) 
            throw new Exception("VM inline operand expected to have string data!");
        
        return data.Value;
    }
}