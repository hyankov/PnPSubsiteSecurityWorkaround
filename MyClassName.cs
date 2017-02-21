namespace My.Assembly.NameSpace
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.SharePoint.Client;
    using OfficeDevPnP.Core.Diagnostics;
    using OfficeDevPnP.Core.Framework.Provisioning.Extensibility;
    using OfficeDevPnP.Core.Framework.Provisioning.Model;
    using OfficeDevPnP.Core.Framework.Provisioning.ObjectHandlers;
    using OfficeDevPnP.Core.Framework.Provisioning.ObjectHandlers.TokenDefinitions;

    public class MyClassName : IProvisioningExtensibilityHandler
    {
        private readonly string logSource = "My.Assembly.NameSpace.MyClassName";

        public void Provision(ClientContext ctx, ProvisioningTemplate template, ProvisioningTemplateApplyingInformation applyingInformation, TokenParser tokenParser, PnPMonitoredScope scope, string configurationData)
        {
            Log.Info(
                this.logSource,
                "ProcessRequest. Template: {0}. Config: {1}",
                template.Id,
                configurationData);

            Web web;
            TokenParser parser = tokenParser;
            var usingRootWeb = false;

            if (ctx.Web.IsSubSite())
            {
                // This extension only works on sub webs with unique permissions!
                ctx.Web.Context.Load(ctx.Web, o => o.HasUniqueRoleAssignments);
                ctx.Web.Context.ExecuteQueryRetry();

                if (!ctx.Web.HasUniqueRoleAssignments)
                {
                    scope.LogDebug("Sub web does not have unique permissions, skipping Security provisioning on it!");
                    return;
                }
                else
                {
                    web = ctx.GetSiteCollectionContext().Site.RootWeb;
                    usingRootWeb = true;
                }
            }
            else
            {
                // Business as usual
                web = ctx.Web;
            }

            var siteSecurity = template.Security;

            var ownerGroup = web.AssociatedOwnerGroup;
            var memberGroup = web.AssociatedMemberGroup;
            var visitorGroup = web.AssociatedVisitorGroup;


            web.Context.Load(ownerGroup, o => o.Title, o => o.Users);
            web.Context.Load(memberGroup, o => o.Title, o => o.Users);
            web.Context.Load(visitorGroup, o => o.Title, o => o.Users);

            web.Context.ExecuteQueryRetry();

            if (!ownerGroup.ServerObjectIsNull.Value)
            {
                AddUserToGroup(web, ownerGroup, siteSecurity.AdditionalOwners, scope);
            }
            if (!memberGroup.ServerObjectIsNull.Value)
            {
                AddUserToGroup(web, memberGroup, siteSecurity.AdditionalMembers, scope);
            }
            if (!visitorGroup.ServerObjectIsNull.Value)
            {
                AddUserToGroup(web, visitorGroup, siteSecurity.AdditionalVisitors, scope);
            }

            foreach (var siteGroup in siteSecurity.SiteGroups)
            {
                Group group = null;
                var allGroups = web.Context.LoadQuery(web.SiteGroups.Include(gr => gr.LoginName));
                web.Context.ExecuteQueryRetry();

                string parsedGroupTitle = parser.ParseString(siteGroup.Title);
                string parsedGroupOwner = parser.ParseString(siteGroup.Owner);
                string parsedGroupDescription = parser.ParseString(siteGroup.Description);

                if (!web.GroupExists(parsedGroupTitle))
                {
                    scope.LogDebug("Creating group {0}", parsedGroupTitle);
                    group = web.AddGroup(
                        parsedGroupTitle,
                        parsedGroupDescription,
                        parsedGroupTitle == parsedGroupOwner);
                    group.AllowMembersEditMembership = siteGroup.AllowMembersEditMembership;
                    group.AllowRequestToJoinLeave = siteGroup.AllowRequestToJoinLeave;
                    group.AutoAcceptRequestToJoinLeave = siteGroup.AutoAcceptRequestToJoinLeave;

                    if (parsedGroupTitle != parsedGroupOwner)
                    {
                        Principal ownerPrincipal = allGroups.FirstOrDefault(gr => gr.LoginName == parsedGroupOwner);
                        if (ownerPrincipal == null)
                        {
                            ownerPrincipal = web.EnsureUser(parsedGroupOwner);
                        }
                        group.Owner = ownerPrincipal;

                    }
                    group.Update();
                    web.Context.ExecuteQueryRetry();
                }
                else
                {
                    group = web.SiteGroups.GetByName(parsedGroupTitle);
                    web.Context.Load(group,
                        g => g.Title,
                        g => g.Description,
                        g => g.AllowMembersEditMembership,
                        g => g.AllowRequestToJoinLeave,
                        g => g.AutoAcceptRequestToJoinLeave,
                        g => g.Owner.LoginName);
                    web.Context.ExecuteQueryRetry();
                    var isDirty = false;
                    if (!String.IsNullOrEmpty(group.Description) && group.Description != parsedGroupDescription)
                    {
                        group.Description = parsedGroupDescription;
                        isDirty = true;
                    }
                    if (group.AllowMembersEditMembership != siteGroup.AllowMembersEditMembership)
                    {
                        group.AllowMembersEditMembership = siteGroup.AllowMembersEditMembership;
                        isDirty = true;
                    }
                    if (group.AllowRequestToJoinLeave != siteGroup.AllowRequestToJoinLeave)
                    {
                        group.AllowRequestToJoinLeave = siteGroup.AllowRequestToJoinLeave;
                        isDirty = true;
                    }
                    if (group.AutoAcceptRequestToJoinLeave != siteGroup.AutoAcceptRequestToJoinLeave)
                    {
                        group.AutoAcceptRequestToJoinLeave = siteGroup.AutoAcceptRequestToJoinLeave;
                        isDirty = true;
                    }
                    if (group.Owner.LoginName != parsedGroupOwner)
                    {
                        if (parsedGroupTitle != parsedGroupOwner)
                        {
                            Principal ownerPrincipal = allGroups.FirstOrDefault(gr => gr.LoginName == parsedGroupOwner);
                            if (ownerPrincipal == null)
                            {
                                ownerPrincipal = web.EnsureUser(parsedGroupOwner);
                            }
                            group.Owner = ownerPrincipal;
                        }
                        else
                        {
                            group.Owner = group;
                        }
                        isDirty = true;
                    }
                    if (isDirty)
                    {
                        scope.LogDebug("Updating existing group {0}", group.Title);
                        group.Update();
                        web.Context.ExecuteQueryRetry();
                    }
                }
                if (group != null && siteGroup.Members.Any())
                {
                    AddUserToGroup(web, group, siteGroup.Members, scope);
                }
            }

            foreach (var admin in siteSecurity.AdditionalAdministrators)
            {
                var user = web.EnsureUser(admin.Name);
                user.IsSiteAdmin = true;
                user.Update();
                web.Context.ExecuteQueryRetry();
            }

            if (siteSecurity.SiteSecurityPermissions != null)
            {
                var existingRoleDefinitions = web.Context.LoadQuery(web.RoleDefinitions.Include(wr => wr.Name, wr => wr.BasePermissions, wr => wr.Description));
                web.Context.ExecuteQueryRetry();

                if (siteSecurity.SiteSecurityPermissions.RoleDefinitions.Any())
                {
                    foreach (var templateRoleDefinition in siteSecurity.SiteSecurityPermissions.RoleDefinitions)
                    {
                        var siteRoleDefinition = existingRoleDefinitions.FirstOrDefault(erd => erd.Name == parser.ParseString(templateRoleDefinition.Name));
                        if (siteRoleDefinition == null)
                        {
                            scope.LogDebug("Creation role definition {0}", parser.ParseString(templateRoleDefinition.Name));
                            var roleDefinitionCI = new RoleDefinitionCreationInformation();
                            roleDefinitionCI.Name = parser.ParseString(templateRoleDefinition.Name);
                            roleDefinitionCI.Description = parser.ParseString(templateRoleDefinition.Description);
                            BasePermissions basePermissions = new BasePermissions();

                            foreach (var permission in templateRoleDefinition.Permissions)
                            {
                                basePermissions.Set(permission);
                            }

                            roleDefinitionCI.BasePermissions = basePermissions;

                            web.RoleDefinitions.Add(roleDefinitionCI);
                            web.Context.ExecuteQueryRetry();
                        }
                        else
                        {
                            var isDirty = false;
                            if (siteRoleDefinition.Description != parser.ParseString(templateRoleDefinition.Description))
                            {
                                siteRoleDefinition.Description = parser.ParseString(templateRoleDefinition.Description);
                                isDirty = true;
                            }
                            var templateBasePermissions = new BasePermissions();
                            templateRoleDefinition.Permissions.ForEach(p => templateBasePermissions.Set(p));
                            if (siteRoleDefinition.BasePermissions != templateBasePermissions)
                            {
                                isDirty = true;
                                foreach (var permission in templateRoleDefinition.Permissions)
                                {
                                    siteRoleDefinition.BasePermissions.Set(permission);
                                }
                            }
                            if (isDirty)
                            {
                                scope.LogDebug("Updating role definition {0}", parser.ParseString(templateRoleDefinition.Name));
                                siteRoleDefinition.Update();
                                web.Context.ExecuteQueryRetry();
                            }
                        }
                    }
                }

                if (usingRootWeb)
                {
                    web = ctx.Web;
                }
                var webRoleDefinitions = web.Context.LoadQuery(web.RoleDefinitions);
                var groups = web.Context.LoadQuery(web.SiteGroups.Include(g => g.LoginName));
                web.Context.ExecuteQueryRetry();

                if (siteSecurity.SiteSecurityPermissions.RoleAssignments.Any())
                {
                    foreach (var roleAssignment in siteSecurity.SiteSecurityPermissions.RoleAssignments)
                    {
                        Principal principal = groups.FirstOrDefault(g => g.LoginName == parser.ParseString(roleAssignment.Principal));
                        if (principal == null)
                        {
                            principal = web.EnsureUser(parser.ParseString(roleAssignment.Principal));
                        }

                        var roleDefinitionBindingCollection = new RoleDefinitionBindingCollection(web.Context);

                        var roleDefinition = webRoleDefinitions.FirstOrDefault(r => r.Name == parser.ParseString(roleAssignment.RoleDefinition));

                        if (roleDefinition != null)
                        {
                            roleDefinitionBindingCollection.Add(roleDefinition);
                        }
                        web.RoleAssignments.Add(principal, roleDefinitionBindingCollection);
                        web.Context.ExecuteQueryRetry();
                    }
                }
            }
        }

        public ProvisioningTemplate Extract(ClientContext ctx, ProvisioningTemplate template, ProvisioningTemplateCreationInformation creationInformation, PnPMonitoredScope scope, string configurationData)
        {
            return template;
        }

        public IEnumerable<TokenDefinition> GetTokens(ClientContext ctx, ProvisioningTemplate template, string configurationData)
        {
            return new List<TokenDefinition>();
        }

        private static void AddUserToGroup(Web web, Group group, IEnumerable<OfficeDevPnP.Core.Framework.Provisioning.Model.User> members, PnPMonitoredScope scope)
        {
            if (members.Any())
            {
                scope.LogDebug("Adding users to group {0}", group.Title);
                try
                {
                    foreach (var user in members)
                    {
                        scope.LogDebug("Adding user {0}", user.Name);
                        var existingUser = web.EnsureUser(user.Name);
                        group.Users.AddUser(existingUser);

                    }
                    web.Context.ExecuteQueryRetry();
                }
                catch (Exception ex)
                {
                    scope.LogError(ex, ex.Message);
                    throw;
                }
            }
        }
    }
}