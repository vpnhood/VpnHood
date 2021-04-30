<template>
  <v-bottom-sheet
    inset
    v-model="sheetVisible"
    value="true"
    max-width="600"
    scrollable
  >
    <v-card rounded class="rounded-t-xl">
      <v-toolbar>
        <v-btn icon @click="sheetVisible = false">
          <v-icon small>close</v-icon>
        </v-btn>
        <v-toolbar-title class="pl-0">
          {{ $t("servers") }}
        </v-toolbar-title>
        <v-spacer></v-spacer>
        <v-btn text rounded color="#23c99d" @click="showAddServerSheet">
          <v-icon class="mx-2">add_circle</v-icon>
          {{ $t("addServer") }}
        </v-btn>
      </v-toolbar>

      <v-card-text class="">
        <!-- Server lists -->
        <v-list>
          <v-list-item
            @click="connect(item.id)"
            rounded
            class="my-4 rounded-lg py-2 server-item"
            :style="
              store.clientProfile.isDefault(item.id)
                ? 'border-style: solid; border-color:#23c99d'
                : ''
            "
            v-for="(item, i) in store.clientProfileItems"
            :key="i"
          >
            <v-list-item-icon class="mr-3" v-if="store.clientProfile.isDefault(item.id)">
              <v-icon
                size="30"
                class="active-icon"
                >done_all</v-icon
              >
            </v-list-item-icon>

            <v-list-item-content>
              <v-list-item-title
                class="font-weight-bold mb-2"
                v-text="store.clientProfile.name(item.id)"
              />
              <v-list-item-subtitle
                v-text="
                  store.clientProfile.ip(item.clientProfile.clientProfileId)
                "
              />
            </v-list-item-content>

            <v-list-item-action>
              <!-- Menu -->
              <ContextMenu
                :clientProfileId="item.clientProfile.clientProfileId"
                :showAddServerItem="false"
                :showManageServerItem="false"
              />
            </v-list-item-action>
          </v-list-item>
        </v-list>
      </v-card-text>
    </v-card>
  </v-bottom-sheet>
</template>

<style scoped>
.server-item {
  box-shadow: 0 1px 2px 1px rgb(0 0 0 / 15%);
  background-color: #eceffb;
}

.active-icon {
  color: var(--master-green);
}

.active-icon:before {
  content: "";
  position: absolute;
  width: 25px;
  height: 25px;
  top: 50%;
  margin-top: -10px;
  margin-left: -5px;
  background-color: #23c99d66;
  border-radius: 50%;
}
</style>

<script>
import ContextMenu from "./ClientProfileMenu";

export default {
  name: 'ServersPage',
  components: {
    ContextMenu
  },
  created() {
    this.isRouterBusy = false;
  },
  beforeDestroy() {
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
