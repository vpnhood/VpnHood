<template>
  <v-bottom-sheet
    inset
    v-model="sheetVisible"
    value="true"
    max-width="600"
    scrollable
  >
    <v-card>
      <v-toolbar>
        <v-toolbar-title>
          {{ $t("servers") }}
        </v-toolbar-title>
        <v-spacer></v-spacer>
        <v-btn text color="primary" @click="showAddServerSheet">
          {{ $t("addServer") }}
        </v-btn>
        <v-btn text color="secondary" @click="sheetVisible = false">
          {{ $t("close") }}
        </v-btn>
      </v-toolbar>

      <v-card-text class="pa-0">
        <!-- Server lists -->
        <v-container>
          <v-row dense>
            <v-col
              v-for="(item, i) in store.clientProfileItems"
              :key="i"
              cols="12"
            >
              <v-expand-transition>
                <v-card
                  :loading="
                    store.connectionState(item.clientProfile.clientProfileId) !=
                      'None' &&
                    store.connectionState(item.clientProfile.clientProfileId) !=
                      'Connected'
                      ? store.state.hasDiagnoseStarted
                        ? 'warning'
                        : true
                      : false
                  "
                  :color="
                    store.connectionState(item.clientProfile.clientProfileId) ==
                    'Connected'
                      ? 'green accent-1'
                      : ''
                  "
                  style="transition: background-color 1s"
                >
                  <div class="d-flex flex-no-wrap justify-space-between">
                    <div>
                      <v-card-title
                        v-text="
                          store.clientProfile.name(
                            item.clientProfile.clientProfileId
                          )
                        "
                      />
                      <v-card-subtitle v-text="item.token.ep" />
                      <v-card-text>
                        {{ store.connectionStateText(item) }}
                      </v-card-text>
                      <v-card-actions>
                        <v-btn
                          v-if="
                            store.state.activeClientProfileId !=
                            item.clientProfile.clientProfileId
                          "
                          class="ma-2"
                          @click="connect(item.clientProfile.clientProfileId)"
                        >
                          {{ $t("connect") }}
                        </v-btn>
                        <v-btn
                          v-else
                          class="ma-2"
                          @click="store.disconnect()"
                          :disabled="
                            store.connectionState(
                              item.clientProfile.clientProfileId
                            ) == 'None'
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
                            editClientProfile(
                              item.clientProfile.clientProfileId
                            )
                          "
                        >
                          <v-list-item-icon>
                            <v-icon>edit</v-icon>
                          </v-list-item-icon>
                          <v-list-item-title>{{
                            $t("rename")
                          }}</v-list-item-title>
                        </v-list-item>

                        <!-- Diagnose -->
                        <v-list-item
                          link
                          @click="
                            store.diagnose(item.clientProfile.clientProfileId)
                          "
                          :disabled="store.state.hasDiagnoseStarted"
                        >
                          <v-list-item-icon>
                            <v-icon>network_check</v-icon>
                          </v-list-item-icon>
                          <v-list-item-title>{{
                            $t("diagnose")
                          }}</v-list-item-title>
                        </v-list-item>

                        <!-- Delete -->
                        <v-divider />
                        <v-list-item
                          link
                          @click="remove(item.clientProfile.clientProfileId)"
                        >
                          <v-list-item-icon>
                            <v-icon>delete</v-icon>
                          </v-list-item-icon>
                          <v-list-item-title>{{
                            $t("remove")
                          }}</v-list-item-title>
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
                <v-btn @click="showAddServerSheet()" class="ma-16" text>
                  {{ $t("addServer") }}
                </v-btn>
              </v-card>
            </v-col>
          </v-row>
        </v-container>
      </v-card-text>
    </v-card>
  </v-bottom-sheet>
</template>

<script>
export default {
  name: 'ServersPage',
  components: {
  },
  created() {
    this.store.setTitle(this.$t("home"));
    this.isRouterBusy = false;
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
  }),
  computed: {
    sheetVisible: {
      get() {
        return this.$route.query.servers != null;
      },
      set(value) {
        if (!value && !this.isRouterBusy) {
          this.isRouterBusy = true;
          this.$router.back();
        }
      }
    }
  },
  watch:
  {
    "$route"() {
      this.isRouterBusy = false;
    }
  },
  methods: {
    async remove(clientProfileId) {
      clientProfileId = this.store.clientProfile.updateId(clientProfileId);
      const res = await this.$confirm(this.$t("confirmRemoveServer"), { title: this.$t("warning") })
      if (res) {
        await this.store.invoke("removeClientProfile", { clientProfileId });
        this.store.loadApp();
      }
    },

    editClientProfile(clientProfileId) {
      window.gtag('event', "editprofile");
      clientProfileId = this.store.clientProfile.updateId(clientProfileId);
      this.$router.push({ path: this.$route.path, query: { ... this.$route.query, editprofile: clientProfileId } })
    },

    showAddServerSheet() {
      window.gtag('event', "addServerButton");
      this.$router.push({ path: this.$route.path, query: { ... this.$route.query, addserver: '1' } })
    },

    connect(clientProfileId) {
      this.store.connect(clientProfileId);
      this.$router.push({ path: '/home' })
    },

  }
}
</script>
