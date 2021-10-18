import i18n from "./i18n";
import store from "./store";

export default {
    items: [],

    item(clientProfileId) {
        let updatedClientProfileId = this.updateId(clientProfileId);
        let ret = this.items.find(x => x.clientProfile.clientProfileId == updatedClientProfileId);
        if (!ret)
            throw `Could not find clientProfileId: ${clientProfileId}`;
        return ret;
    },

    exists(clientProfileId) {
        let updatedClientProfileId = this.updateId(clientProfileId);
        let ret = this.items.find(x => x.clientProfile.clientProfileId == updatedClientProfileId);
        return ret != null;
    },

    updateId(clientProfileId) {

        if (clientProfileId == '$') {
            clientProfileId = store.state.defaultClientProfileId;
            let res = this.items.find(x => x.clientProfile.clientProfileId == clientProfileId);
            if (!res && this.items.length > 0)
                clientProfileId = this.items[0].clientProfile.clientProfileId;
        }

        return clientProfileId;
    },

    profile(clientProfileId) {
        clientProfileId = this.updateId(clientProfileId);
        return this.item(clientProfileId).clientProfile;
    },

    defaultProfile() {
        if (!this.items || this.items.length == 0 || !store.state || !store.state.defaultClientProfileId)
            return null;
        return this.profile(store.state.defaultClientProfileId);
    },

    name(clientProfileId) {
        clientProfileId = this.updateId(clientProfileId);

        let clientProfileItem = this.item(clientProfileId);
        let clientProfile = clientProfileItem.clientProfile;
        if (clientProfile.name && clientProfile.name.trim() != '') return clientProfile.name;
        else if (clientProfileItem.token.name && clientProfileItem.token.name.trim() != '') return clientProfileItem.token.name;
        else return i18n.t('noname');
    },

    ip(clientProfileId) {
        clientProfileId = this.updateId(clientProfileId);

        let clientProfileItem = this.item(clientProfileId);
        let token = clientProfileItem.token;
        var endPoints = token.ep!=null ? token.ep.join(",") : null;
        return token && token.isv ? `${token.hname}:${token.hport}` : endPoints;
    },

    isDefault(clientProfileId) {
        let defaultProfile = this.defaultProfile();
        return defaultProfile && defaultProfile.clientProfileId == clientProfileId;
    }
}
