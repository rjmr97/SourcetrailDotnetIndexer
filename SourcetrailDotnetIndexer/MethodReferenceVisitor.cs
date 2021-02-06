﻿using CoatiSoftware.SourcetrailDB;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SourcetrailDotnetIndexer
{
    /// <summary>
    /// A visitor for references found in IL-code of a method
    /// </summary>
    class MethodReferenceVisitor
    {
        private readonly BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private readonly Assembly assembly;
        private readonly TypeHandler typeHandler;
        private readonly DataCollector dataCollector;

        /// <summary>
        /// Invoked when a method needs to be parsed
        /// </summary>
        public EventHandler<CollectedMethodEventArgs> ParseMethod;

        public MethodReferenceVisitor(Assembly assembly, TypeHandler typeHandler, DataCollector dataCollector)
        {
            this.assembly = assembly;
            this.typeHandler = typeHandler;
            this.dataCollector = dataCollector;
        }

        public void VisitMethodCall(MethodBase originatingMethod, MethodBase calledMethod, int methodId, int classId)
        {
            var targetClassId = 0;
            // do not collect members of foreign assemblies
            if (calledMethod.DeclaringType.Assembly == assembly)
            {
                targetClassId = typeHandler.AddToDbIfValid(calledMethod.DeclaringType);
            }
            // check, if this is an async method
            if (targetClassId == 0 && calledMethod.DeclaringType.IsAsyncStateMachineOf(originatingMethod, out var asyncWorker))
            {
                // dive into the async worker, as that is where the "meat" of the method is
                ParseMethod?.Invoke(this, new CollectedMethodEventArgs(new CollectedMethod(asyncWorker, methodId, classId)));
            }
            // collect method parameters
            // note that we just assume the parameters are used, e.g. the code may actually pass null as an argument
            // to get this right, we would have to look at the actual arguments, that get passed to the method
            // this is a bit more involved, maybe something for a future version...
            // (i think, i'll leave it this way, the value passed may be a parameter itself, so we can never be sure in these cases)
            foreach (var mParam in calledMethod.GetParameters())
            {
                var paramTypeId = typeHandler.AddToDbIfValid(mParam.ParameterType);
                if (paramTypeId > 0 && paramTypeId != classId)  // ignore self-references (e.g. when passing "this" as a parameter)
                {
                    dataCollector.CollectReference(classId, paramTypeId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    dataCollector.CollectReference(methodId, paramTypeId, ReferenceKind.REFERENCE_TYPE_USAGE);
                }
            }
            if (targetClassId > 0)
            {
                if (classId != targetClassId)   // ignore self-references
                {
                    dataCollector.CollectReference(classId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    dataCollector.CollectReference(methodId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                }
                var targetMethodId = typeHandler.CollectMember(calledMethod, out var targetKind);
                if (targetMethodId > 0)
                    dataCollector.CollectReference(methodId, targetMethodId,
                        targetKind == SymbolKind.SYMBOL_METHOD ? ReferenceKind.REFERENCE_CALL : ReferenceKind.REFERENCE_USAGE);
                // if target is an interface, link to implementors as well
                // (seems to be consistent with behavior of VS 2019 when looking at references)
                List<Type> implementors = new List<Type>();
                if (calledMethod.DeclaringType.IsInterface)
                    implementors.AddRange(typeHandler.GetInterfaceImplementors(calledMethod.DeclaringType));
                foreach (var implementor in implementors)
                {
                    var implTypeId = typeHandler.AddToDbIfValid(implementor);
                    if (implTypeId > 0)
                    {
                        dataCollector.CollectReference(classId, implTypeId, ReferenceKind.REFERENCE_TYPE_USAGE);
                        dataCollector.CollectReference(methodId, implTypeId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    }
                    foreach (var implMethod in implementor.GetMethods(flags))
                    {
                        // use correct overload
                        if (implMethod.Name == calledMethod.Name && implMethod.HasSameParameters(calledMethod))
                        {
                            var implMethodId = typeHandler.CollectMember(implMethod, out var targetImplKind);
                            dataCollector.CollectReference(methodId, implMethodId,
                                targetImplKind == SymbolKind.SYMBOL_METHOD ? ReferenceKind.REFERENCE_CALL : ReferenceKind.REFERENCE_USAGE);
                            break;
                        }
                    }
                }
            }
        }

        public void VisitFieldReference(FieldInfo referencedField, int methodId, int classId)
        {
            var targetClassId = typeHandler.AddToDbIfValid(referencedField.DeclaringType);
            if (targetClassId > 0)
            {
                if (classId != targetClassId)   // ignore self-references
                {
                    dataCollector.CollectReference(classId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    dataCollector.CollectReference(methodId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                }
                var fieldId = typeHandler.CollectMember(referencedField, out _);
                if (fieldId > 0)
                {
                    //dataCollector.CollectReference(classId, fieldId, ReferenceKind.REFERENCE_USAGE);
                    dataCollector.CollectReference(methodId, fieldId, ReferenceKind.REFERENCE_USAGE);
                }
                var fieldTypeId = typeHandler.AddToDbIfValid(referencedField.FieldType);
                if (fieldTypeId > 0)
                {
                    if (classId != fieldTypeId)
                    {
                        dataCollector.CollectReference(classId, fieldTypeId, ReferenceKind.REFERENCE_TYPE_USAGE);
                        dataCollector.CollectReference(methodId, fieldTypeId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    }
                }
            }
        }

        public void VisitTypeReference(Type type, int methodId, int classId)
        {
            var targetClassId = typeHandler.AddToDbIfValid(type);
            if (targetClassId > 0)
            {
                if (classId != targetClassId)       // ignore self-references
                {
                    dataCollector.CollectReference(classId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    dataCollector.CollectReference(methodId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                }
            }
        }

        public void VisitMethodReference(MethodBase originatingMethod, MethodBase referencedMethod, int methodId, int classId)
        {
            var targetClassId = 0;
            // do not collect members of foreign assemblies
            if (referencedMethod.DeclaringType.Assembly == assembly)
                targetClassId = typeHandler.AddToDbIfValid(referencedMethod.DeclaringType);
            if (targetClassId > 0)
            {
                if (classId != targetClassId)       // ignore self-references
                {
                    dataCollector.CollectReference(classId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    dataCollector.CollectReference(methodId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                }
                var targetMethodId = typeHandler.CollectMember(referencedMethod, out _);
                if (targetMethodId > 0)
                    dataCollector.CollectReference(methodId, targetMethodId, ReferenceKind.REFERENCE_USAGE);
            }
            // attempt to detect inline lambdas (e.g. foo.Select(item => bar(item))
            // or lambda methods in a compiler-generated class
            // NOTE: not sure, if we should collect it here, as we have only a REFERENCE to a method, not a method-call
            // (do not collect for now)
            //if (referencedMethod.IsLambdaOf(originatingMethod))
            //{
            //    ParseMethod?.Invoke(this, new CollectedMethodEventArgs(new CollectedMethod(referencedMethod, methodId, classId)));
            //}
        }
    }
}
