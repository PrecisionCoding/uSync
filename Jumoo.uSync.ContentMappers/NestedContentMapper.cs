﻿using System.Linq;

using Umbraco.Core;
using Umbraco.Core.Services;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Jumoo.uSync.Core;
using Jumoo.uSync.Core.Mappers;

namespace Jumoo.uSync.ContentMappers
{
    public class NestedContentMapper : IContentMapper
    {
        private IContentTypeService _contentTypeService;
        private IDataTypeService _dataTypeService;

        public NestedContentMapper()
        {
            _contentTypeService = ApplicationContext.Current.Services.ContentTypeService;
            _dataTypeService = ApplicationContext.Current.Services.DataTypeService;
        }

        public string GetExportValue(int dataTypeDefinitionId, string value)
        {
            var array = JsonConvert.DeserializeObject<JArray>(value);
            if (array == null || !array.Any())
                return value; 

            foreach(var nestedObject in array)
            {
                var doctype = _contentTypeService.GetContentType(nestedObject["ncContentTypeAlias"].ToString());
                if (doctype == null)
                    continue;

                foreach (var propertyType in doctype.CompositionPropertyTypes)
                {
                    object alias = nestedObject[propertyType.Alias];
                    if (alias != null)
                    {
                        var dataType = _dataTypeService.GetDataTypeDefinitionById(propertyType.DataTypeDefinitionId);
                        if (dataType != null)
                        {
                            IContentMapper mapper = ContentMapperFactory.GetMapper(dataType.PropertyEditorAlias);
                            if (mapper != null)
                            {
                                nestedObject[propertyType.Alias] =
                                    mapper.GetExportValue(dataType.Id, nestedObject[propertyType.Alias].ToString());
                            }
                        }
                    }
                }
            }

            return JsonConvert.SerializeObject(array, Formatting.Indented);
        }

        public string GetImportValue(int dataTypeDefinitionId, string content)
        {
            var array = JsonConvert.DeserializeObject<JArray>(content);
            if (array == null || !array.Any())
                return content;

            foreach (var nestedObject in array)
            {
                var doctype = _contentTypeService.GetContentType(nestedObject["ncContentTypeAlias"].ToString());
                if (doctype == null)
                    continue;

                foreach (var propertyType in doctype.CompositionPropertyTypes)
                {
                    object alias = nestedObject[propertyType.Alias];
                    if (alias != null)
                    {
                        var dataType = _dataTypeService.GetDataTypeDefinitionById(propertyType.DataTypeDefinitionId);
                        if (dataType != null)
                        {
                            IContentMapper mapper = ContentMapperFactory.GetMapper(dataType.PropertyEditorAlias);
                            if (mapper != null)
                            {
                                nestedObject[propertyType.Alias] =
                                    mapper.GetImportValue(dataType.Id, nestedObject[propertyType.Alias].ToString());
                            }
                        }
                    }
                }
            }

            return JsonConvert.SerializeObject(array, Formatting.Indented);

        }
    }
}
