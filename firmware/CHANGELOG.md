# Changelog

## [0.4.0](https://github.com/owellnitz/plant-monitor/compare/firmware-v0.3.0...firmware-v0.4.0) (2026-07-24)


### Features

* **firmware:** add OTA partition table and firmware build id ([e9bc3ed](https://github.com/owellnitz/plant-monitor/commit/e9bc3ed3abc91545cecfb8daf06954cd6ff1af99))

## [0.3.0](https://github.com/owellnitz/plant-monitor/compare/firmware-v0.2.0...firmware-v0.3.0) (2026-07-19)


### ⚠ BREAKING CHANGES

* **firmware:** after flashing, the sensor publishes under its MAC id, so it shows up as a new unassigned sensor — rebind it to the plant in the app.

### Features

* **firmware:** derive device id from STA MAC ([f8c8203](https://github.com/owellnitz/plant-monitor/commit/f8c8203a73ba213183f3110eb4c4ba5e380df195))


### Bug Fixes

* **firmware:** bound wifi bring-up with 30 s deadline ([7443907](https://github.com/owellnitz/plant-monitor/commit/7443907e87d30470f4e5a994957c546f167379c9))
* **firmware:** calibrate deep-sleep timer drift ([cdf2bdd](https://github.com/owellnitz/plant-monitor/commit/cdf2bdde902b1bc2302b72b5b0fbb6a9eaad6d24))

## [0.2.0](https://github.com/owellnitz/plant-monitor/compare/firmware-v0.1.0...firmware-v0.2.0) (2026-07-18)


### Features

* **firmware:** drop net status screens from OLED ([20decc8](https://github.com/owellnitz/plant-monitor/commit/20decc80eda398524b58ccf76161eaf40431b3fc))


### Bug Fixes

* **firmware:** don't hang when MQTT broker is unreachable ([ca62e9c](https://github.com/owellnitz/plant-monitor/commit/ca62e9cc7d3d71d60c297f35c6369c7038bf8ec4))
* **firmware:** full WiFi teardown and interrupt-free deep sleep entry ([b2f79fc](https://github.com/owellnitz/plant-monitor/commit/b2f79fc3177ec32661e6d1a1b6d79267f1228eca))
* **firmware:** skip publish when broker unreachable ([4f8140f](https://github.com/owellnitz/plant-monitor/commit/4f8140f447cf5f85981c17d66fb86385173dfde2))
* **firmware:** stop WiFi before deep sleep, report reset reason ([1ea2be2](https://github.com/owellnitz/plant-monitor/commit/1ea2be29c802ae2cc83f9934ccff3370e612abfc))
* **firmware:** time out CONNACK wait to avoid wedged device ([9deb6f0](https://github.com/owellnitz/plant-monitor/commit/9deb6f0d72d60110cdc17a629febf5c158702eb5))
* **firmware:** time out CONNACK wait to avoid wedged device ([4e4596a](https://github.com/owellnitz/plant-monitor/commit/4e4596ae280b85a3ddacacda2be8e9660b124f28))
* **firmware:** time out MQTT connect when broker unreachable ([9236d1e](https://github.com/owellnitz/plant-monitor/commit/9236d1e15f58e2d3c1ca1af75349b5e8afa435e5))
* **firmware:** wait for broker ACK before MQTT teardown ([1e2ecdf](https://github.com/owellnitz/plant-monitor/commit/1e2ecdf30df45e91c6d630eac73c9ca0a5fac685))
* pin embedded-io to 0.6 to restore net build ([8be3ce1](https://github.com/owellnitz/plant-monitor/commit/8be3ce1eac4e6b5675bc643364f4ca2f909efe56))
* reliable MQTT publish and duplicate-free readings ([2698ae6](https://github.com/owellnitz/plant-monitor/commit/2698ae6d56dcd19f523e0a3dce1e48e1ffcb4638))


### Reverts

* **firmware:** drop reset-reason field from payload ([965f2e0](https://github.com/owellnitz/plant-monitor/commit/965f2e00081b453803534ddf56a7fbf6e03e4023))
