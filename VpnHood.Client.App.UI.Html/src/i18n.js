import Vue from 'vue'
import VueI18n from 'vue-i18n'
import langEn from 'vuetify/es5/locale/en'
Vue.use(VueI18n);


const messages = {
    en: {
        $vuetify: langEn,
        isRtl: "false",
        appName: "VpnHood",
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
        noname: "Server",
        noServerSelected: "No Server",
        selectedServer: "Server:",
        manageServers: "Change",
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
        selectServerTitle: "Select or Add a server"
    },
};

// Create VueI18n instance with options
export const i18n = new VueI18n({
    locale: 'en', // set locale
    fallbackLocale: 'en',
    messages, // set locale messages
});

export default i18n;