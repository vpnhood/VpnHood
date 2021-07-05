import Vue from 'vue'
import VueI18n from 'vue-i18n'
import langEn from 'vuetify/es5/locale/en'
Vue.use(VueI18n);


const messages = {
    en: {
        $vuetify: langEn,
        isRtl: "false",
        appName: "VpnHood!",
        settings: "Settings",
        add: "Add",
        help: "Help",
        home: 'Home',
        version: "Version",
        connect: "Connect",
        connecting: "Connecting...",
        connected: "Connected",
        disconnect: "Disconnect",
        disconnected: "Disconnected",
        disconnecting: "Disconnecting...",
        diagnosing: "Diagnosing...",
        addServer: "Add Server",
        diagnose: "Diagnose",
        darkMode: "Dark Mode",
        clientProfile: "VPN Profile",
        clientProfileName: "Name",
        remove: "Delete",
        rename: "Rename",
        warning: "Warning",
        cancel: "Cancel",
        close: "close",
        save: "Save",
        ok: "OK",
        noname: "Server",
        uploadSpeed: "Up",
        downloadSpeed: "Down",
        noServerSelected: "No Server",
        selectedServer: "Server",
        manageServers: "Change Server",
        change: "Change",
        feedback: "Send Feedback",
        openReport: "Open Report",
        sendReport: "Send Report",
        confirmRemoveServer:"Do you really want to remove this server?<br/><br/>{serverName}",
        addTestServer: "Add public server",
        addTestServerSubtitle: "You have removed the Public Test Server. This server is free and for evaluation.",
        addAcessKeyTitle: "Add private access key",
        addAcessKeySubtitle: "Copy and paste an access key to add a server.",
        invalidAccessKeyFormat: "The accessKey has invalid format. make sure it starts with {prefix}",
        servers: "Servers",
        selectServerTitle: "Select or Add a server",
        publicServerWarningTitle: "Public Server Hint",
        publicServerWarning: "It is a connection to public servers created for evaluation. It may be slow or not accessible sometimes.<br/><br/>To have a reliable and fast connection, you need to connect to Private Servers.",
        dontShowMessage: "Don't show this message again.",
        appFilter: "Allowed Apps",
        appFilterDesc: "Which apps can use VPN?",
        appFilterStatus_title: "Apps",
        appFilterStatus_all: "All apps",
        appFilterStatus_exclude: "All except {x} apps",
        appFilterStatus_include: "Only {x} apps",
        appFilterAll: "All apps",
        appFilterInclude: "Only selected apps",
        appFilterExclude: "All apps except selected",
        ipFilter: "Allowed Countries",
        ipFilterDesc: "Which countries can use VPN?",
        ipFilterStatus_title: "Countries",
        ipFilterStatus_all: "All countries",
        ipFilterStatus_exclude: "All except {x} countries",
        ipFilterStatus_include: "Only {x} countries",
        ipFilterAll: "All countries",
        ipFilterInclude: "Only selected countries",
        ipFilterExclude: "All countries except selected",
        protocol: "Protocol",
        protocol_title: "Protocol",
        protocol_desc: "UDP is much faster for apps that heavily use it, such as torrents, but it may not work with some firewalls and proxies. If you disable UDP, all apps still work, but they may get much slower.",
        protocol_udpOn: "Use UDP (Faster)",
        protocol_udpOff: "No UDP (Slower)",
        selectedApps: "Selected Apps",
        selectedIpGroups: "Selected Countries",
        newServerAdded: "A new server has been added.",
        changelog: "What's New?"
    },
};

// Create VueI18n instance with options
export const i18n = new VueI18n({
    locale: 'en', // set locale
    fallbackLocale: 'en',
    messages, // set locale messages
});

export default i18n;