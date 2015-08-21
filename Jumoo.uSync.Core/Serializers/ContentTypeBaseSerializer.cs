﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Services;

using Jumoo.uSync.Core.Interfaces;
using Jumoo.uSync.Core.Extensions;
using Umbraco.Core.Logging;

namespace Jumoo.uSync.Core.Serializers
{
    abstract public class ContentTypeBaseSerializer<T> : SyncBaseSerializer<T>, ISyncSerializerTwoPass<T>
    {
        internal IContentTypeService _contentTypeService;

        public ContentTypeBaseSerializer(string itemType): base(itemType)
        {
            _contentTypeService = ApplicationContext.Current.Services.ContentTypeService;
        }

        #region ContentTypeBase Deserialize Helpers

        /// <summary>
        ///  does the basic deserialization, bascially the stuff in info
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        internal void DeserializeBase(IContentTypeBase item, XElement info)
        {
            var alias = info.Element("Alias").Value;
            if (item.Alias != alias)
                item.Alias = alias;

            var name = info.Element("Name").ValueOrDefault("");
            if (item.Name != name)
                item.Name = item.Name;


            var icon = info.Element("Icon").ValueOrDefault("");
            if (item.Icon != icon)
                item.Icon = icon;

            var thumb = info.Element("Thumbnail").ValueOrDefault("");
            if (item.Thumbnail != thumb)
                item.Thumbnail = thumb;

            var desc = info.Element("Description").ValueOrDefault("");
            if (item.Description != desc)
                item.Description = desc;

            var allow = info.Element("AllowAtRoot").ValueOrDefault(false);
            if (item.AllowedAsRoot != allow)
                item.AllowedAsRoot = allow;

            var masterAlias = info.Element("Master").ValueOrDefault(string.Empty);
            if (!string.IsNullOrEmpty(masterAlias))
            {
                var master = default(IContentTypeBase);
                if ( _itemType == Constants.Packaging.DocumentTypeNodeName)
                    master = _contentTypeService.GetContentType(masterAlias);
                else
                    master = _contentTypeService.GetMediaType(masterAlias);

                if (master != null)
                {
                    item.SetLazyParentId(new Lazy<int>(() => master.Id));                        
                }
            }
        }

        internal void DeserializeStructure(IContentTypeBase item, XElement node)
        {
            var structureNode = node.Element("Structure");
            if (structureNode == null)
                return;

            List<ContentTypeSort> allowedTypes = new List<ContentTypeSort>();
            int sortOrder = 0;

            foreach (var contentBaseNode in structureNode.Elements(_itemType))
            {
                var alias = contentBaseNode.Value;
                var key = contentBaseNode.Attribute("Key").ValueOrDefault(Guid.Empty);

                IContentTypeBase contentBaseItem = default(IContentTypeBase);
                if (key != Guid.Empty)
                {
                    LogHelper.Debug<uSync.Core.Events>("Using key to find structure element");

                    // by key search (survives renames)
                    if (_itemType == Constants.Packaging.DocumentTypeNodeName)
                        contentBaseItem = _contentTypeService.GetContentType(key);
                    else
                        contentBaseItem = _contentTypeService.GetMediaType(key);
                }

                if (contentBaseItem == null && !string.IsNullOrEmpty(alias))
                {
                    LogHelper.Debug<uSync.Core.Events>("Fallback Alias lookup");
                    if (_itemType == Constants.Packaging.DocumentTypeNodeName)
                    {
                        contentBaseItem = _contentTypeService.GetContentType(alias);
                    }
                    else
                    {
                        contentBaseItem = _contentTypeService.GetMediaType(alias);
                    }
                }

                if (contentBaseItem != default(IContentTypeBase))
                {
                    allowedTypes.Add(new ContentTypeSort(
                        new Lazy<int>(() => contentBaseItem.Id), sortOrder, contentBaseItem.Name));
                    sortOrder++;
                }
            }

            item.AllowedContentTypes = allowedTypes;
        }

        internal void DeserializeProperties(IContentTypeBase item, XElement node)
        {
            List<string> propertiesToRemove = new List<string>();
            Dictionary<string, string> propertiesToMove = new Dictionary<string, string>();
            Dictionary<PropertyGroup, string> tabsToBlank = new Dictionary<PropertyGroup, string>();


            var propertyNodes = node.Elements("GenericProperties").Elements("GenericProperty");

            foreach(var property in item.PropertyTypes)
            {
                XElement propertyNode = propertyNodes
                                            .SingleOrDefault(x => x.Element("Key").Value == property.Key.ToString());

                if (propertyNode == null)
                {
                    LogHelper.Debug<uSync.Core.Events>("Looking up property type by alias");
                    propertyNode = propertyNodes
                        .SingleOrDefault(x => x.Element("Alias").Value == property.Alias);
                }

                if (propertyNodes == null)
                {
                    propertiesToRemove.Add(property.Alias);
                }
                else
                {
                    if (propertyNode.Element("Key") != null)
                    {
                        Guid key = Guid.Empty;
                        if (Guid.TryParse(propertyNode.Element("Key").Value, out key))
                            property.Key = key;

                    }
                    // update existing settings.
                    if (propertyNode.Element("Name") != null)
                        property.Name = propertyNode.Element("Name").Value;

                    if (propertyNode.Element("Description") != null)
                        property.Description = propertyNode.Element("Description").Value;

                    if (propertyNode.Element("Mandatory") != null)
                        property.Mandatory = propertyNode.Element("Mandatory").Value.ToLowerInvariant().Equals("true");

                    if (propertyNode.Element("Validation") != null)
                        property.ValidationRegExp= propertyNode.Element("Validation").Value;

                    if (propertyNode.Element("SortOrder") != null)
                        property.SortOrder = int.Parse(propertyNode.Element("SortOrder").Value);

                    if (propertyNode.Element("Tab") != null)
                    {
                        var nodeTab = propertyNode.Element("Tab").Value;
                        if (!string.IsNullOrEmpty(nodeTab))
                        {
                            var propGroup = item.PropertyGroups.FirstOrDefault(x => x.Name == nodeTab);

                            if (propGroup != null)
                            {
                                if (!propGroup.PropertyTypes.Any(x => x.Alias == property.Alias))
                                {
                                    // this tab currently doesn't contain this property, to we have to
                                    // move it (later)
                                    propertiesToMove.Add(property.Alias, nodeTab);
                                }
                            }
                        }
                        else
                        {
                            // this property isn't in a tab (now!)

                            var existingTab = item.PropertyGroups.FirstOrDefault(x => x.PropertyTypes.Contains(property));
                            if (existingTab != null)
                            {
                                // this item is now not in a tab (when it was)
                                // so we have to remove it from tabs (later)
                                tabsToBlank.Add(existingTab, property.Alias);
                            }
                        }

                    }
                }
            }


            // now we have gone through all the properties, we can do the moves and removes from the groups
            if (propertiesToMove.Any())
            {
                foreach (var move in propertiesToMove)
                {
                    item.MovePropertyType(move.Key, move.Value);
                }
            }

            if (propertiesToRemove.Any())
            {
                // removing properties can cause timeouts on installs with lots of content...
                foreach(var delete in propertiesToRemove)
                {
                    item.RemovePropertyType(delete);
                }
            }

            if (tabsToBlank.Any())
            {
                foreach(var blank in tabsToBlank)
                {
                    // there might be a bug here, we need to do some cheking of if this is 
                    // possible with the public api

                    // blank.Key.PropertyTypes.Remove(blank.Value);
                }
            }

        }

        internal void DeserializeTabSortOrder(IContentTypeBase item, XElement node)
        {
            var tabNode = node.Element("Tabs");

            foreach(var tab in tabNode.Elements("Tab"))
            {
                var name = tab.Element("Caption").Value;
                var sortOrder = tab.Element("SortOrder");

                if (sortOrder != null)
                {
                    if (!string.IsNullOrEmpty(sortOrder.Value))
                    {
                        var itemTab = item.PropertyGroups.FirstOrDefault(x => x.Name == name);
                        if (itemTab != null)
                        {
                            itemTab.SortOrder = int.Parse(sortOrder.Value);
                        }
                    }
                }
            }

            // remove tabs 
            List<string> tabsToRemove = new List<string>();
            foreach(var tab in item.PropertyGroups)
            {
                if (tabNode.Elements("Tab").FirstOrDefault(x => x.Element("Caption").Value == tab.Name) == null)
                {
                    // no tab of this name in the import... remove it.
                    tabsToRemove.Add(tab.Name);
                }
            }

            foreach (var name in tabsToRemove)
            {
                item.PropertyGroups.Remove(name);
            }            
        }
#endregion

#region ContentTypeBase Serialize Helpers
        internal XElement SerializeInfo(IContentTypeBase item)
        {
            var info = new XElement("Info",
                            new XElement("Key", item.Key),
                            new XElement("Name", item.Name),
                            new XElement("Alias", item.Alias),
                            new XElement("Icon", item.Icon),
                            new XElement("Thumbnail", item.Thumbnail),
                            new XElement("Description", item.Description),
                            new XElement("AllowAtRoot", item.AllowedAsRoot.ToString()),
                            new XElement("IsListView", item.IsContainer.ToString()));

            return info;
        }

        internal XElement SerializeTabs(IContentTypeBase item)
        {
            var tabs = new XElement("Tabs");
            foreach (var tab in item.PropertyGroups.OrderBy(x => x.SortOrder))
            {
                tabs.Add(new XElement("Tab",
                        // new XElement("Key", tab.Key),
                        new XElement("Caption", tab.Name),
                        new XElement("SortOrder", tab.SortOrder)));
            }

            return tabs;
        }

        /// <summary>
        ///  So fiddling with the structure
        /// 
        ///  In an umbraco export the structure can come out in a random order
        ///  for consistancy, and better tracking of changes we export the list
        ///  in alias order, that way it should always be the same every time
        ///  regardless of the creation order of the doctypes.
        /// 
        ///  In earlier versions of umbraco, the structure export didn't always
        ///  work - so we redo the export, if it turns out this is fixed in 7.3
        ///  we shoud just do the xml sort like with properties, it will be faster
        /// </summary>
        internal XElement SerializeStructure(IContentTypeBase item)
        {
            var structureNode = new XElement("Structure");

            LogHelper.Info<MediaTypeSerializer>("BASE: Content Types: {0}", () => item.AllowedContentTypes.Count());

            SortedList<string, Guid> allowedAliases = new SortedList<string, Guid>();
            foreach(var allowedType in item.AllowedContentTypes)
            {
                IContentTypeBase allowed = null;         
                if (_itemType == Constants.Packaging.DocumentTypeNodeName)
                {
                    allowed = _contentTypeService.GetContentType(allowedType.Id.Value);
                }
                else
                {
                    allowed = _contentTypeService.GetMediaType(allowedType.Id.Value);
                }

                if (allowed != null)
                    allowedAliases.Add(allowed.Alias, allowed.Key);
            }


            foreach (var alias in allowedAliases)
            {
                structureNode.Add(new XElement(_itemType, alias.Key,
                    new XAttribute("Key", alias.Value.ToString()))
                    );
            }
            return structureNode;            
        }

        /// <summary>
        ///  as with structure, we want to export properties in a consistant order
        ///  this just jiggles the order of the generic properties section, ordering by name
        /// 
        ///  at the moment we are making quite a big assumption that name is always there?
        /// </summary>
        internal XElement SerializeProperties(IContentTypeBase item)
        {
            var _dataTypeService = ApplicationContext.Current.Services.DataTypeService;

            var properties = new XElement("GenericProperties");

            foreach(var property in item.PropertyTypes.OrderBy(x => x.Name))
            {
                var propNode = new XElement("GenericProperty");

                propNode.Add(new XElement("Key", property.Key));
                propNode.Add(new XElement("Name", property.Name));
                propNode.Add(new XElement("Alias", property.Alias));

                var def = _dataTypeService.GetDataTypeDefinitionById(property.DataTypeDefinitionId);
                if (def != null)
                    propNode.Add(new XElement("Definition", def.Key));

                propNode.Add(new XElement("Type", property.PropertyEditorAlias));
                propNode.Add(new XElement("Mandatory", property.Mandatory));

                if (property.ValidationRegExp != null)
                    propNode.Add(new XElement("Validation", property.ValidationRegExp));

                if (property.Description != null)
                    propNode.Add(new XElement("Description", new XCData(property.Description)));

                propNode.Add(new XElement("SortOrder", property.SortOrder));

                var tab = item.PropertyGroups.FirstOrDefault(x => x.PropertyTypes.Contains(property));
                propNode.Add(new XElement("Tab", tab != null ? tab.Name : ""));

                properties.Add(propNode);
            }

            return properties;
        }

    
        // special case for two pass, you can tell it to only first step
        public SyncAttempt<T> DeSerialize(XElement node, bool forceUpdate, bool onePass = false)
        {
            var attempt = base.DeSerialize(node);

            if (!onePass || !attempt.Success || attempt.Item == null)
                return attempt;

            return DesearlizeSecondPass(attempt.Item, node);
        }

        virtual public SyncAttempt<T> DesearlizeSecondPass(T item, XElement node)
        {
            return SyncAttempt<T>.Succeed(node.NameFromNode(), ChangeType.NoChange);
        }

#endregion
    }
}