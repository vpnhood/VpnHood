import i18n from "./i18n";
import store from "./store";

export default {
    items: [],

    item(clientProfileId) {
        if (clientProfileId == '$') clientProfileId = store.state.defaultClientProfileId;
        let ret = this.items.find(x => x.clientProfile.clientProfileId == clientProfileId);
        if (!ret)
            throw `Could not find clientProfileId: ${clientProfileId}`;
        return ret;
    },

    profile(clientProfileId) {
        return this.item(clientProfileId).clientProfile;
    },

    defaultProfile() {
        if (!this.items || this.items.length == 0 || !store.state || !store.state.defaultClientProfileId)
            return null;
        return this.profile(store.state.defaultClientProfileId);
    },

    connectionState(clientProfileId) {
        return (store.state.activeClientProfileId == clientProfileId)
            ? store.state.connectionState
            : "None";
    },

    statusText(clientProfileId) {
        switch (this.connectionState(clientProfileId)) {
            case "Connecting": return i18n.t('connecting');
            case "Connected": return i18n.t('connected');
            case "Disconnecting": return i18n.t('disconnecting');
            case "Diagnosing": return i18n.t('diagnosing');
            default: return i18n.t('disconnected');
        }
    },

    name(clientProfileId) {
        let clientProfileItem = this.item(clientProfileId);
        let clientProfile = clientProfileItem.clientProfile;
        if (clientProfile.name && clientProfile.name.trim() != '') return clientProfile.name;
        else if (clientProfileItem.token.name && clientProfileItem.token.name.trim() != '') return clientProfileItem.token.name;
        else return i18n.t('noname');
    },

}
