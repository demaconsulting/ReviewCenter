// Views rendered into docs/design/generated/ for the Design document.
// Reopens the ReviewCenter package so each 'expose' statement can reference
// its target part def directly. Per SysML2Tools syntax, 'expose' is only valid
// inside a named 'view' usage (not a 'view def' definition), and it - not
// 'render' - is what scopes the rendered diagram content.
package ReviewCenter {
    view SoftwareStructureView {
        expose ReviewCenter;
    }

    view ReviewCenterView {
        expose ReviewCenterSystem;
    }

    view CliView {
        expose CliSubsystem;
    }

    view HostingView {
        expose HostingSubsystem;
    }

    view WebView {
        expose WebSubsystem;
    }

    view IdentityView {
        expose IdentitySubsystem;
    }

    view CoreView {
        expose CoreSubsystem;
    }

    view StorageView {
        expose StorageSubsystem;
    }

    view SelfTestView {
        expose SelfTestSubsystem;
    }

    view UtilitiesView {
        expose UtilitiesSubsystem;
    }
}

// Reopens the separate ReviewCenterPuppet package (the Puppet dev tool software system).
package ReviewCenterPuppet {
    view ReviewCenterPuppetView {
        expose ReviewCenterPuppetSystem;
    }
}
