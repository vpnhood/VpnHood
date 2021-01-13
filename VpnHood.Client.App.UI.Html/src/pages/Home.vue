<template>
  <v-card max-width="600" class="mx-auto" flat>
    <v-dialog
      v-if="isEditProfile"
      v-model="isEditProfile"
      :transition="isMobileSize ? 'dialog-bottom-transition' : ''"
      :max-width="isMobileSize ? '' : 600"
      :fullscreen="isMobileSize"
    >
      <ClientProfile :clientProfileId="$route.query.editprofile" />
    </v-dialog>

    <!-- Server lists -->
    <v-container>
      <v-row dense>
        <v-col v-for="(item, i) in items" :key="i" cols="12">
          <v-expand-transition>
            <v-card
              v-if="
                !store.state.activeClientProfileId ||
                store.state.activeClientProfileId ==
                  item.clientProfile.clientProfileId
              "
              :loading="
                clientProfileItem_state(item) == 'Connecting' ||
                clientProfileItem_state(item) == 'Disconnecting'
                  ? store.state.hasDiagnoseStarted
                    ? 'warning'
                    : true
                  : false
              "
              :color="
                clientProfileItem_state(item) == 'Connected'
                  ? $vuetify.theme.dark
                    ? 'green accent-4'
                    : 'green accent-1'
                  : ''
              "
              style="transition: background-color 1s"
            >
              <div class="d-flex flex-no-wrap justify-space-between">
                <div>
                  <v-card-title v-text="getProfileItemName(item)" />
                  <v-card-subtitle v-text="item.token.ep" />
                  <v-card-text>
                    {{ clientProfileItem_statusText(item) }}
                  </v-card-text>
                  <v-card-actions>
                    <v-btn
                      v-if="
                        store.state.activeClientProfileId !=
                        item.clientProfile.clientProfileId
                      "
                      class="ma-2"
                      @click="connect(item)"
                    >
                      {{ $t("connect") }}
                    </v-btn>
                    <v-btn
                      v-else
                      class="ma-2"
                      @click="disconnect()"
                      :disabled="
                        clientProfileItem_state(item) != 'Connecting' &&
                        clientProfileItem_state(item) != 'Connected'
                      "
                    >
                      {{ $t("disconnect") }}
                    </v-btn>
                  </v-card-actions>
                </div>

                <!-- Item Menu -->
                <v-menu left transition="slide-y-transition">
                  <template v-slot:activator="{ on, attrs }">
                    <v-btn v-bind="attrs" v-on="on" icon>
                      <v-icon>mdi-dots-vertical</v-icon>
                    </v-btn>
                  </template>
                  <v-list>
                    <!-- Rename -->
                    <v-list-item
                      link
                      @click="
                        renameValue = getProfileItemName(item);
                        renameClientProfile = item.clientProfile;
                        renameDialog = true;
                      "
                    >
                      <v-list-item-title>{{ $t("rename") }}</v-list-item-title>
                    </v-list-item>

                    <!-- Diagnose -->
                    <v-list-item link @click="diagnose(item)" :disabled="store.state.hasDiagnoseStarted">
                      <v-list-item-title>{{
                        $t("diagnose")
                      }}</v-list-item-title>
                    </v-list-item>

                    <!-- Delete -->
                    <v-divider />
                    <v-list-item link @click="remove(item)">
                      <v-list-item-title>{{ $t("remove") }}</v-list-item-title>
                    </v-list-item>
                  </v-list>
                </v-menu>
              </div>
            </v-card>
          </v-expand-transition>
        </v-col>

        <!-- Add Server Button -->
        <v-col cols="12" v-if="!store.state.activeClientProfileId">
          <v-card class="text-center">
            <v-btn
              @click="showAddServerSheet('addServerButton')"
              class="ma-16"
              text
            >
              {{ $t("addServer") }}
            </v-btn>
          </v-card>
        </v-col>
      </v-row>
    </v-container>

    <!-- rename dialog -->
    <v-dialog v-model="renameDialog" max-width="400px">
      <v-card>
        <v-card-title>
          {{ $t("rename") }}
        </v-card-title>
        <v-card-text>
          <v-text-field
            v-model="renameValue"
            label=""
            spellcheck="false"
            autocomplete="off"
            required
          ></v-text-field>
        </v-card-text>
        <v-card-actions>
          <v-spacer></v-spacer>
          <v-btn color="blue darken-1" text @click="renameDialog = false">
            {{ $t("cancel") }}
          </v-btn>
          <v-btn
            color="blue darken-1"
            text
            @click="
              renameDialog = false;
              rename(renameClientProfile, renameValue);
            "
          >
            {{ $t("save") }}
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Add Server Sheet -->
    <v-bottom-sheet v-model="addServerSheet">
      <v-sheet>
        <!-- Add Test Server -->
        <v-card
          v-if="showAddTestServer"
          class="mx-auto ma-5"
          max-width="600"
          flat
        >
          <v-card-title>{{ $t("addTestServer") }}</v-card-title>
          <v-card-subtitle>{{ $t("addTestServerSubtitle") }}</v-card-subtitle>
          <v-btn text @click="addTestServer()">{{ $t("add") }}</v-btn>
          <v-divider class="mt-5" />
        </v-card>

        <!-- Add Private Server -->
        <v-card class="mx-auto" max-width="600" flat>
          <v-card-title>{{ $t("addAcessKeyTitle") }}</v-card-title>
          <v-card-subtitle>{{ $t("addAcessKeySubtitle") }}</v-card-subtitle>
          <v-text-field
            v-model="accessKeyValue"
            spellcheck="false"
            autocomplete="off"
            :error-messages="accessKeyErrorMessage"
            class="mx-5"
            @input="onKeyAccessChanged"
            append-icon="vpn_key"
            :placeholder="accessKeyPrefix"
            solo
          ></v-text-field>
        </v-card>
      </v-sheet>
    </v-bottom-sheet>
  </v-card>
</template>

<script>
import { Base64 } from 'js-base64';

export default {
  name: 'ServersPage',
  components: {
  },
  created() {
    this.store.setTitle(this.$t("home"));
    this.updateToolbars();
    this.monitorId = setInterval(() => {
      if (!document.hidden)
        this.store.updateState();
    }, 1000);

  },
  beforeDestroy() {
    clearInterval(this.monitorId);
    this.monitorId = 0;
  },
  data: () => ({
    accessKeyPrefix: "vh://",
    addServerSheet: false,
    accessKeyValue: null,
    accessKeyErrorMessage: null,
    renameDialog: false,
    renameValue: null,
    renameClientProfile: null,
  }),
  computed: {
    items() { return this.store.clientProfileItems; },
    isEditProfile:
    {
      get() { return this.$route.query.editprofile },
      set(value) {
        if (!value && this.$route.query.editprofile) {
          this.$router.push('/home');
        }
      }
    },
    isMobileSize() { return this.$vuetify.breakpoint.smAndDown; },
    showAddTestServer() {
      return this.store.features.testServerTokenId &&
        !this.store.clientProfileItems.find(x => x.clientProfile.tokenId == this.store.features.testServerTokenId);
    }
  },
  methods: {
    clientProfileItem_state(item) {
      console.log(this.store.state);
      if (this.store.state.activeClientProfileId != item.clientProfile.clientProfileId) return "Disconnected";
      if (this.store.state.isDiagnosing) return "Diagnosing";
      var ret = this.store.state.clientState;
      if (ret == "None" || ret == "Disposed") ret = "Disconnected";
      return ret;
    },

    clientProfileItem_statusText(item) {
      switch (this.clientProfileItem_state(item)) {
        case "Connecting": return this.$t('connecting');
        case "Connected": return this.$t('connected');
        case "Disconnecting": return this.$t('disconnecting');
        case "Diagnosing": return this.$t('diagnosing');
        default: return this.$t('disconnected');
      }
    },

    connect(item) {
      window.gtag('event', 'connect');
      this.store.state.isDiagnosedStarted = false;
      this.store.state.activeClientProfileId = item.clientProfile.clientProfileId;
      this.store.invoke("connect", { clientProfileId: item.clientProfile.clientProfileId });
    },

    diagnose(item) {
      window.gtag('event', 'diagnose');
      this.store.state.isDiagnosedStarted = true;
      this.store.state.activeClientProfileId = item.clientProfile.clientProfileId;
      this.store.invoke("diagnose", { clientProfileId: item.clientProfile.clientProfileId });
    },

    disconnect() {
      window.gtag('event', 'disconnect');
      this.store.state.clientState = this.$t("disconnecting");
      this.store.invoke("disconnect");
    },

    async onKeyAccessChanged(value) {
      this.accessKeyErrorMessage = null;

      if (value == null || value == "")
        return;

      if (!this.validateAccessKey(value)) {
        this.accessKeyErrorMessage = this.$t("invalidAccessKeyFormat", { prefix: this.accessKeyPrefix });
        return;
      }

      try {
        await this.store.invoke("addAccessKey", { accessKey: value })
        this.addServerSheet = false;
        this.store.loadApp();
      }
      catch (ex) {
        this.accessKeyErrorMessage = ex.toString();
      }
    },

    validateAccessKey(accessKey) {
      try {
        const json = Base64.decode(accessKey);
        return JSON.parse(json) != null;
      }
      catch (ex) {
        return false;
      }
    },
    async rename(clientProfile, name) {
      clientProfile.name = name;
      await this.store.invoke("setClientProfile", { clientProfile });
      this.store.loadApp();
    },

    async remove(item) {
      const clientProfileId = item.clientProfile.clientProfileId;
      const res = await this.$confirm(this.$t("confirmRemoveServer"), { title: this.$t("warning") })
      if (res) {
        await this.store.invoke("removeClientProfile", { clientProfileId });
        this.store.loadApp();
      }
    },
    getProfileItemName(item) {
      if (item.clientProfile.name && item.clientProfile.name.trim() != '') return item.clientProfile.name;
      else if (item.token.name && item.token.name.trim() != '') return item.token.name;
      else return this.$t('noname');

    },
    async addTestServer() {
      await this.store.invoke("addTestServer");
      await this.store.loadApp();
      this.addServerSheet = false;
    },
    showAddServerSheet(source) {
      window.gtag('event', source);
      this.addServerSheet = !this.addServerSheet;
      this.accessKeyValue = null;
      this.accessKeyErrorMessage = null;
    },

    updateToolbars() {
      this.store.toolbarItems = [
        { tooltip: this.$t("addServer"), icon: "add", click: () => this.showAddServerSheet('addServerToolBar') }
      ]
    },
  }
}
</script>
