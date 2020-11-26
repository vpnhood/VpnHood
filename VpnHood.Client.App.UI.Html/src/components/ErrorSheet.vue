<template>
  <v-bottom-sheet v-model="errorSheet" hide-overlay dark flat>
    <v-sheet color="grey darken-2" class="pa-2">
      <v-icon>error_outline</v-icon>
      {{ store.state.lastError }}
      <br />

      <!-- Close -->
      <v-btn class="ma-2" @click="errorSheet = false">
        {{ $t("close") }}
      </v-btn>

      <!-- Diagnose -->
      <v-btn
        class="ma-2"
        @click="diagnose()"
        v-if="
          !store.state.logExists && this.store.state.lastActiveClientProfileId
        "
      >
        {{ $t("diagnose") }}
      </v-btn>

      <!-- OpenReport -->
      <v-btn
        class="ma-2"
        :href="this.store.serverUrl + '/api/log.txt'"
        target="_blank"
        v-if="store.state.logExists"
      >
        {{ $t("openReport") }}
      </v-btn>

      <!-- SendReport -->
      <v-btn
        class="ma-2"
        target="_blank"
        @click="sendReport()"
        v-if="store.state.logExists"
        >{{ $t("sendReport") }}
      </v-btn>
    </v-sheet>
  </v-bottom-sheet>
</template>

<script>

import firebase from "firebase/app";


export default {
  components: {
  },
  created() {
  },
  mounted() {
  },
  data: () => ({
  }),

  computed: {
    errorSheet: {
      get() {
        return this.store.state.lastError != null &&
          (!this.store.state.isDisconnectedByUser || this.store.state.isDiagnoseStarted);
      },
      set: function (value) {
        if (value == false) {
          this.store.invoke("clearLastError");
        }
      }
    }
  },

  methods: {
    diagnose() {
      window.gtag('event', 'diagnose');
      this.store.state.isDiagnosedStarted = true;
      const clientProfileId = this.store.state.lastActiveClientProfileId;
      this.store.state.activeClientProfileId = clientProfileId;
      this.store.invoke("diagnose", { clientProfileId });
    },

    uuidv4() {
      return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        var r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
      });
    },

    async sendReport() {
      const reportId = this.uuidv4();
      const link = `https://docs.google.com/forms/d/e/1FAIpQLSeOT6vs9yTqhAONM2rJg8Acae-oPZTecoVrdPrzJ-3VsgJk0A/viewform?usp=sf_link&entry.450665336=${reportId}`;

      // get report
      const url = this.store.serverUrl + '/api/log.txt';
      const response = await fetch(url);
      const log = await response.text();

      // Create a root reference
      var storageRef = firebase.storage().ref();
      const spacePath = `logs/client/${reportId}.txt`;
      var spaceRef = storageRef.child(spacePath);

      spaceRef.putString(log).then(function () {
        console.log('Report has been sent!'); // eslint-disable-line no-console
        window.open(link, reportId);
      });


    }
  }
}
</script>
