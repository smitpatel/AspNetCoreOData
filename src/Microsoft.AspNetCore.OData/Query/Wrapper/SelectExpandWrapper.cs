﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Microsoft.AspNetCore.OData.Edm;
using Microsoft.AspNetCore.OData.Formatter.Value;
using Microsoft.AspNetCore.OData.Query.Container;
using Microsoft.OData.Edm;

namespace Microsoft.AspNetCore.OData.Query.Wrapper
{
    internal abstract class SelectExpandWrapper : IEdmEntityObject, ISelectExpandWrapper
    {
        private static readonly IPropertyMapper DefaultPropertyMapper = new IdentityPropertyMapper();
        private static readonly Func<IEdmModel, IEdmStructuredType, IPropertyMapper> _mapperProvider =
            (IEdmModel m, IEdmStructuredType t) => DefaultPropertyMapper;

        private Dictionary<string, object> _containerDict;
        private TypedEdmStructuredObject _typedEdmStructuredObject;

        /// <summary>
        /// Gets or sets the property container that contains the properties being expanded. 
        /// </summary>
        public PropertyContainer Container { get; set; }

        /// <summary>
        /// The model associated with the request.
        /// </summary>
        public IEdmModel Model { get; set; }

        /// <inheritdoc />
        public object UntypedInstance { get; set; }

        /// <summary>
        /// Gets or sets the instance type name
        /// </summary>
        public string InstanceType { get; set; }

        /// <summary>
        /// Indicates whether the underlying instance can be used to obtain property values.
        /// </summary>
        public bool UseInstanceForProperties { get; set; }

        /// <inheritdoc />
        public IEdmTypeReference GetEdmType()
        {
            if (InstanceType != null)
            {
                IEdmStructuredType structuredType = Model.FindType(InstanceType) as IEdmStructuredType;
                IEdmEntityType entityType = structuredType as IEdmEntityType;

                if (entityType != null)
                {
                    return entityType.ToEdmTypeReference(true);
                }

                return structuredType.ToEdmTypeReference(true);
            }

            Type elementType = GetElementType();

            return Model.GetTypeMappingCache().GetEdmType(elementType, Model);
        }

        /// <inheritdoc />
        public bool TryGetPropertyValue(string propertyName, out object value)
        {
            // look into the container first to see if it has that property. container would have it 
            // if the property was expanded.
            if (Container != null)
            {
                _containerDict = _containerDict ?? Container.ToDictionary(DefaultPropertyMapper, includeAutoSelected: true);
                if (_containerDict.TryGetValue(propertyName, out value))
                {
                    return true;
                }
            }

            // fall back to the instance.
            if (UseInstanceForProperties && UntypedInstance != null)
            {
                IEdmTypeReference edmTypeReference = GetEdmType();
                if (edmTypeReference is IEdmComplexTypeReference)
                {
                    _typedEdmStructuredObject = _typedEdmStructuredObject ??
                        new TypedEdmComplexObject(UntypedInstance, edmTypeReference as IEdmComplexTypeReference, Model);
                }
                else
                {
                    _typedEdmStructuredObject = _typedEdmStructuredObject ??
                        new TypedEdmEntityObject(UntypedInstance, edmTypeReference as IEdmEntityTypeReference, Model);
                }

                return _typedEdmStructuredObject.TryGetPropertyValue(propertyName, out value);
            }

            value = null;
            return false;
        }

        public IDictionary<string, object> ToDictionary()
        {
            return ToDictionary(_mapperProvider);
        }

        public IDictionary<string, object> ToDictionary(Func<IEdmModel, IEdmStructuredType, IPropertyMapper> mapperProvider)
        {
            if (mapperProvider == null)
            {
                throw Error.ArgumentNull("mapperProvider");
            }

            Dictionary<string, object> dictionary = new Dictionary<string, object>();
            IEdmStructuredType type = GetEdmType().AsStructured().StructuredDefinition();

            IPropertyMapper mapper = mapperProvider(Model, type);
            if (mapper == null)
            {
                throw Error.InvalidOperation(SRResources.InvalidPropertyMapper, typeof(IPropertyMapper).FullName,
                    type.FullTypeName());
            }

            if (Container != null)
            {
                dictionary = Container.ToDictionary(mapper, includeAutoSelected: false);
            }

            // The user asked for all the structural properties on this instance.
            if (UseInstanceForProperties && UntypedInstance != null)
            {
                foreach (IEdmStructuralProperty property in type.StructuralProperties())
                {
                    object propertyValue;
                    if (TryGetPropertyValue(property.Name, out propertyValue))
                    {
                        string mappingName = mapper.MapProperty(property.Name);
                        if (String.IsNullOrWhiteSpace(mappingName))
                        {
                            throw Error.InvalidOperation(SRResources.InvalidPropertyMapping, property.Name);
                        }

                        dictionary[mappingName] = propertyValue;
                    }
                }
            }

            return dictionary;
        }

        protected abstract Type GetElementType();
    }
}