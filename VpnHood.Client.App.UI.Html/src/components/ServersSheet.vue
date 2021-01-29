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
        <v-btn small icon @click="sheetVisible = false">
          <v-icon small>close</v-icon>
        </v-btn>
        <v-toolbar-title class="pl-0">
          {{ $t("servers") }}
        </v-toolbar-title>
        <v-spacer></v-spacer>
        <v-btn icon color="primary" @click="showAddServerSheet">
          <v-icon>add</v-icon>
          <!-- {{ $t("add") }} -->
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
                        {{ store.connectionStateText(item.clientProfile.clientProfileId) }}
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

                    <!-- Menu -->
                    <ContextMenu :clientProfileId="item.clientProfile.clientProfileId" :showAddServerItem="false"/>
                  </div>
                </v-card>
              </v-expand-transition>
            </v-col>

            <!-- Add Server Button -->
            <v-col
              cols="12"
              v-if="
                !store.state.activeClientProfileId &&
                store.clientProfile.items.length <= 1
              "
            >
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
import ContextMenu from "./ClientProfileMenu";

export default {
  name: 'ServersPage',
  components: {
    ContextMenu
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
    connect(clientProfileId) {
      this.store.connect(clientProfileId);
      this.$router.push({ path: '/home' })
    },

    showAddServerSheet() {
      window.gtag('event', "addServerButton");
      this.$router.push({ path: this.$route.path, query: { ... this.$route.query, addserver: '1' } })
    },
  }
}
</script>
