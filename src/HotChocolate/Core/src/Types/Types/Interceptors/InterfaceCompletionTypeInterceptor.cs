using System;
using System.Collections.Generic;
using System.Linq;
using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Definitions;

#nullable enable

namespace HotChocolate.Types.Interceptors;

internal class InterfaceCompletionTypeInterceptor : TypeInterceptor
{
    private readonly Dictionary<ITypeSystemObject, TypeInfo> _typeInfos = new();
    private readonly Dictionary<Type, TypeInfo> _allInterfaceRuntimeTypes = new();
    private readonly HashSet<Type> _interfaceRuntimeTypes = new();
    private readonly HashSet<NameString> _completed = new();
    private readonly HashSet<NameString> _completedFields = new();
    private readonly Queue<InterfaceType> _backlog = new();

    public override bool TriggerAggregations => true;

    public override void OnAfterInitialize(
        ITypeDiscoveryContext discoveryContext,
        DefinitionBase? definition,
        IDictionary<string, object?> contextData)
    {
        // we need to preserve the initialization context of all
        // interface types and object types.
        if (definition is IComplexOutputTypeDefinition typeDefinition)
        {
            _typeInfos.Add(discoveryContext.Type, new(discoveryContext, typeDefinition));
        }
    }

    public override void OnTypesInitialized(
        IReadOnlyCollection<ITypeDiscoveryContext> discoveryContexts)
    {
        // after all types have been initialized we will index the runtime
        // types of all interfaces.
        foreach (TypeInfo interfaceTypeInfo in _typeInfos.Values
            .Where(t => t.Definition.RuntimeType is { } rt &&
                rt != typeof(object) &&
                t.Definition is InterfaceTypeDefinition))
        {
            if (!_allInterfaceRuntimeTypes.ContainsKey(interfaceTypeInfo.Definition.RuntimeType))
            {
                _allInterfaceRuntimeTypes.Add(
                    interfaceTypeInfo.Definition.RuntimeType,
                    interfaceTypeInfo);
            }
        }

        // we now will use the runtime types to infer interface usage ...
        foreach (TypeInfo typeInfo in _typeInfos.Values.Where(IsRelevant))
        {
            _interfaceRuntimeTypes.Clear();

            TryInferInterfaceFromRuntimeType(
                GetRuntimeType(typeInfo),
                _allInterfaceRuntimeTypes.Keys,
                _interfaceRuntimeTypes);

            if (_interfaceRuntimeTypes.Count > 0)
            {
                // if we detect that this type implements an interface,
                // we will register it as a dependency.
                foreach (Type interfaceRuntimeType in _interfaceRuntimeTypes)
                {
                    TypeInfo interfaceTypeInfo = _allInterfaceRuntimeTypes[interfaceRuntimeType];
                    var interfaceTypeDependency = new TypeDependency(
                        interfaceTypeInfo.Context.TypeReference, 
                        TypeDependencyKind.Completed); 

                    typeInfo.Context.Dependencies.Add(interfaceTypeDependency);
                    typeInfo.Definition.Interfaces.Add(interfaceTypeDependency.TypeReference);
                }
            }
        }
    }

    // defines if this type has a concrete runtime type.
    private bool IsRelevant(TypeInfo typeInfo)
    {
        if (typeInfo.Definition is ObjectTypeDefinition { IsExtension: true } objectDef &&
            objectDef.FieldBindingType != typeof(object))
        {
            return true;
        }

        Type? runtimeType = typeInfo.Definition.RuntimeType;
        return runtimeType is not null && runtimeType != typeof(object);
    }

    private Type GetRuntimeType(TypeInfo typeInfo)
    {
        if (typeInfo.Definition is ObjectTypeDefinition { IsExtension: true } objectDef)
        {
            return objectDef.FieldBindingType ?? typeof(object);
        }

        return typeInfo.Definition.RuntimeType;
    }

    public override void OnBeforeCompleteType(
        ITypeCompletionContext completionContext,
        DefinitionBase? definition,
        IDictionary<string, object?> contextData)
    {
        if (definition is InterfaceTypeDefinition { Interfaces: { Count: > 0 } } typeDef)
        {
            _completed.Clear();
            _completedFields.Clear();
            _backlog.Clear();

            foreach (ITypeReference? interfaceRef in typeDef.Interfaces)
            {
                if (completionContext.TryGetType(
                    interfaceRef,
                    out InterfaceType? interfaceType))
                {
                    _completed.Add(interfaceType.Name);
                    _backlog.Enqueue(interfaceType);
                }
            }

            foreach (InterfaceFieldDefinition? field in typeDef.Fields)
            {
                _completedFields.Add(field.Name);
            }

            CompleteInterfacesAndFields(typeDef);
        }

        if (definition is ObjectTypeDefinition { Interfaces: { Count: > 0 } } objectTypeDef)
        {
            _completed.Clear();
            _completedFields.Clear();
            _backlog.Clear();

            foreach (ITypeReference? interfaceRef in objectTypeDef.Interfaces)
            {
                if (completionContext.TryGetType(
                    interfaceRef,
                    out InterfaceType? interfaceType))
                {
                    _completed.Add(interfaceType.Name);
                    _backlog.Enqueue(interfaceType);
                }
            }

            foreach (ObjectFieldDefinition? field in objectTypeDef.Fields)
            {
                _completedFields.Add(field.Name);
            }

            CompleteInterfacesAndFields(objectTypeDef);
        }
    }

    private void CompleteInterfacesAndFields(IComplexOutputTypeDefinition definition)
    {
        while (_backlog.Count > 0)
        {
            InterfaceType current = _backlog.Dequeue();
            TypeInfo typeInfo = _typeInfos[current];
            definition.Interfaces.Add(TypeReference.Create(current));

            if (definition is InterfaceTypeDefinition interfaceDef)
            {
                foreach (InterfaceFieldDefinition? field in ((InterfaceTypeDefinition)typeInfo.Definition).Fields)
                {
                    if (_completedFields.Add(field.Name))
                    {
                        interfaceDef.Fields.Add(field);
                    }
                }
            }

            foreach (InterfaceType? interfaceType in current.Implements)
            {
                if (_completed.Add(interfaceType.Name))
                {
                    _backlog.Enqueue(interfaceType);
                }
            }
        }
    }

    private static void TryInferInterfaceFromRuntimeType(
        Type runtimeType,
        ICollection<Type> allInterfaces,
        ICollection<Type> interfaces)
    {
        if (runtimeType == typeof(object))
        {
            return;
        }

        foreach (Type interfaceType in runtimeType.GetInterfaces())
        {
            if (allInterfaces.Contains(interfaceType))
            {
                interfaces.Add(interfaceType);
            }
        }
    }

    private readonly struct TypeInfo
    {
        public TypeInfo(
            ITypeDiscoveryContext context,
            IComplexOutputTypeDefinition definition)
        {
            Context = context;
            Definition = definition;
        }

        public ITypeDiscoveryContext Context { get; }

        public IComplexOutputTypeDefinition Definition { get; }

        public override string? ToString() => Definition.Name;
    }
}
