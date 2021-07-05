import pack from '../package.json';
import i18n from './i18n';
import clientProfile from './clientProfile';

export default {
    newVersion: null,
    cultures: [
        { value: "en", isRtl: false, name: "English", nativeName: "English" },
    ],
    title: i18n.t('appName'),
    serverUrl: process.env.NODE_ENV === 'development' ? "http://127.0.0.1:9090" : window.location.origin,
    features: null,
    state: null,
    settings: null,
    navigationDrawer: false,
    toolbarItems: [],  //{ tooltip:"", icon: "" click=function, disabled=false, hidden=false}
    clientProfileItems: [],
    clientProfile: clientProfile,
    requestedPublicServerProfileId: null,
    installedApps: null,
    ipGroups: null,
    newServerAdded: false,

    navigationItems() {
        return [
            // { title: i18n.t("home"), icon: "home", link: "/home", enabled: true },
            //{ title: i18n.t("servers"), icon: "dns", link: "/servers", enabled: true },
            // { title: i18n.t("help"), icon: "help", link: "/help/help.html", enabled: true },
        ]
    },

    appVersion() {
        return this.features.version + "." + pack.version.split(".")[2];
    },

    setTitle(resourceValue) {
        this.title = resourceValue;
        document.title = i18n.t('appName') + ' - ' + resourceValue;

    },

    updateLayout(vm) {
        i18n.locale = this.userSettings.cultureName;
        //moment.locale(i18n.locale);
        vm.$root.$vuetify.rtl = vm.$t("isRtl") == "true";
        vm.$root.$vuetify.lang.current = this.userSettings.cultureName;
        // vm.$root.$vuetify.theme.dark = this.userSettings.darkMode;
    },

    updateState() {
        return this.loadApp({ withState: true });
    },

    updateClientProfiles() {
        return this.loadApp({ withClientProfileItems: true });
    },

    async loadInstalledApps() {
        let ret = await this.invoke("installedApps");
        this.installedApps = ret.sort((a, b) => a.appName.localeCompare(b.appName, undefined, { sensitivity: 'base' }));
    },

    async loadIpGroups() {
        if (!this.ipGroups) {
            let ret = await this.invoke("ipGroups");
            this.ipGroups = ret.sort((a, b) => {
                if (a.ipGroupName == "Custom") return -1;
                if (b.ipGroupName == "Custom") return +1;
                return a.ipGroupName.localeCompare(b.ipGroupName, undefined, { sensitivity: 'base' });
            });
        }
    },

    async loadApp(options) {
        if (!options)
            options = { withState: true, withFeatures: true, withSettings: true, withClientProfileItems: true };

        const data = await this.invoke("loadApp", options);
        if (options.withState) this.state = data.state;
        if (options.withFeatures) this.features = data.features;
        if (options.withSettings) { this.settings = data.settings; this.userSettings = data.settings.userSettings; }
        if (options.withClientProfileItems) this.clientProfileItems = data.clientProfileItems;

        if (!this.userSettings.appFilters) this.userSettings.appFilters = [];
        this.clientProfile.items = this.clientProfileItems;
    },

    saveUserSettings() {
        this.invoke("setUserSettings", this.userSettings);
    },

    connect(clientProfileId, ignoreHint = false) {
        window.gtag('event', 'connect');

        //do nothing if already connected or connecting
        if (this.state.activeClientProfileId == clientProfileId &&
            (this.state.connectionState == "Connected" || this.state.connectionState == "Connecting"))
            return;

        clientProfileId = this.clientProfile.updateId(clientProfileId);
        this.state.hasDiagnosedStarted = false;
        this.state.activeClientProfileId = clientProfileId;
        this.state.defaultClientProfileId = clientProfileId;
        this.state.connectionState = "Connecting";

        // show hint
        if (!ignoreHint) {

            let item = this.clientProfile.item(clientProfileId);
            if (item.token.pb) {
                this.requestedPublicServerProfileId = clientProfileId;
                return;
            }
        }
        this.requestedPublicServerProfileId = null;
        return this.invoke("connect", { clientProfileId });
    },

    disconnect() {
        window.gtag('event', 'disconnect');
        this.state.connectionState = "Disconnecting";
        return this.invoke("disconnect");
    },

    diagnose(clientProfileId) {
        window.gtag('event', 'diagnose');
        clientProfileId = this.clientProfile.updateId(clientProfileId);
        this.state.hasDiagnosedStarted = true;
        this.state.activeClientProfileId = clientProfileId;
        this.state.defaultClientProfileId = clientProfileId;
        return this.invoke("diagnose", { clientProfileId });
    },

    connectionState(clientProfileId) {
        // if (clientProfileId) return "Diagnosing";
        clientProfileId = this.clientProfile.updateId(clientProfileId);
        return (this.state.activeClientProfileId == clientProfileId)
            ? this.state.connectionState
            : "None";
    },

    connectionStateText(clientProfileId) {
        clientProfileId = this.clientProfile.updateId(clientProfileId);
        switch (this.connectionState(clientProfileId)) {
            case "Connecting": return i18n.t('connecting');
            case "Connected": return i18n.t('connected');
            case "Disconnecting": return i18n.t('disconnecting');
            case "Diagnosing": return i18n.t('diagnosing');
            default: return i18n.t('disconnected');
        }
    },

    async invoke(method, args = {}) {
        var log = `invoke: ${method}`;
        if (args) log + `, args ${args}`;
        console.log(log); // eslint-disable-line no-console
        const response = await this._invokeInterntal(method, args);
        var result = await response.text();
        if (result)
            return JSON.parse(result);
    },

    async _invokeInterntal(method, args = {}) {
        try {
            return await this.invokeApi(method, args);
        }
        catch (ex) {
            this.lastError = ex;
            this.latErrorState = true;
            throw ex;
        }
    },

    async invokeApi(method, args = {}) {
        const url = this.serverUrl + "/api/" + method;
        const response = await this.post(url, args);
        if (response.status == 404) throw `Could not find ${method} api `
        if (response.status != 200) throw `Failed to call the ${method} api. status: ${response.status}`;
        return response;
    },

    //POST method implementation:
    async post(url, args = {}) {
        // Default options are marked with *
        const response = await fetch(url, {
            method: 'POST', // *GET, POST, PUT, DELETE, etc.
            mode: 'cors', // no-cors, *cors, same-origin
            cache: 'no-cache', // *default, no-cache, reload, force-cache, only-if-cached
            credentials: 'same-origin', // include, *same-origin, omit
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json'
                // 'Content-Type': 'application/x-www-form-urlencoded',
            },
            redirect: 'follow', // manual, *follow, error
            referrerPolicy: 'no-referrer', // no-referrer, *no-referrer-when-downgrade, origin, origin-when-cross-origin, same-origin, strict-origin, strict-origin-when-cross-origin, unsafe-url
            body: JSON.stringify(args) // body data type must match "Content-Type" header
        });
        return response;
    }
}
