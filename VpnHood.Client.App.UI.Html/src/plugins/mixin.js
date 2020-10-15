export default {
  created() {
  },
  methods: {
  },
  computed: {
    store() {
      return this.$root.$data.gStore;
    },
    isRtl() {
      return this.$vuetify.rtl == true;
    }
  }
}