# PnP Subsite Security Workaround

Consider the following scenario: you are provisioning a PnP Template against a sub web. e.g.:

<pnp:Provisioning xmlns:pnp="http://schemas.dev.office.com/PnP/2015/12/ProvisioningSchema">
  <pnp:Preferences Generator="OfficeDevPnP.Core, Version=2.3.1604.0, Culture=neutral, PublicKeyToken=3751622786b357c2" />
  <pnp:Templates ID="CONTAINER-TEMPLATE-XXX">
    <pnp:ProvisioningTemplate ID="TEMPLATE-XXX" Version="1">
      <pnp:Security>
        <pnp:SiteGroups>
          <pnp:SiteGroup Title="My Group" Owner="SHAREPOINT\system" AllowMembersEditMembership="false" AllowRequestToJoinLeave="false" AutoAcceptRequestToJoinLeave="false" OnlyAllowMembersViewMembership="true" />
        </pnp:SiteGroups>
        <pnp:Permissions>
          <pnp:RoleAssignments>
            <pnp:RoleAssignment Principal="My Group" RoleDefinition="Full Control" />
          </pnp:RoleAssignments>
        </pnp:Permissions>
      </pnp:Security>
    </pnp:ProvisioningTemplate>
  </pnp:Templates>
</pnp:Provisioning>

A PnP extension, which will perform the Security element provisioning on the sub web exactly the way it should be – if the web is a sub web and the permission inheritance is broken – go ahead with the Security provisioning, instead of ignoring it.
