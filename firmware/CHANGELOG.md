# Changelog

## [0.9.0](https://github.com/owellnitz/plant-monitor/compare/firmware-v0.8.0...firmware-v0.9.0) (2026-11-05)


### Features

* **firmware:** halve the wake LED brightness ([d1c092c](https://github.com/owellnitz/plant-monitor/commit/d1c092c060695c9b1cc7ea8b36a3a20ec8199cf8))


### Bug Fixes

* **firmware:** give the WS2812 its reset time before the first frame ([6458404](https://github.com/owellnitz/plant-monitor/commit/645840435bb815a3502998977d1cf9c77d145677))

## [0.8.0](https://github.com/owellnitz/plant-monitor/compare/firmware-v0.7.0...firmware-v0.8.0) (2026-11-04)


### Features

* **firmware:** add calibrate binary for sensor endpoints ([7c1f6dd](https://github.com/owellnitz/plant-monitor/commit/7c1f6dd071e3b959f0a3fb1b1794e2e87ac12354))
* **firmware:** default to headless when no display is configured ([d04cb24](https://github.com/owellnitz/plant-monitor/commit/d04cb24d80021d9c214c019ae1a2512054038262))
* **firmware:** parse an optional display key from the config partition ([181e8cb](https://github.com/owellnitz/plant-monitor/commit/181e8cb1913f4e34fd134053a310272492104b2b))
* **firmware:** read per-device sensor calibration from config partition ([bbaef54](https://github.com/owellnitz/plant-monitor/commit/bbaef544aecab70f0604d66de616acf15367baa5))
* **firmware:** report a rejected calibration in the MQTT payload ([38e15a4](https://github.com/owellnitz/plant-monitor/commit/38e15a4fc17d7cc1c600128d3ef76791191fbf5f))
* **firmware:** send cal_rejected only when the calibration is rejected ([876a53a](https://github.com/owellnitz/plant-monitor/commit/876a53a7899614ce60b8cc4958ee2d031006de60))
* **firmware:** skip the OLED when no display is provisioned ([0d6595e](https://github.com/owellnitz/plant-monitor/commit/0d6595e219823665d01e08680f76c241ba19889e))


### Bug Fixes

* **firmware:** accept quoted, commented and upper-case display values ([7465b4d](https://github.com/owellnitz/plant-monitor/commit/7465b4d429efdaaad95d5412ada2231dd958801d))
* **firmware:** calibrate rejects a dry value below MIN_DRY in step 1 ([c689078](https://github.com/owellnitz/plant-monitor/commit/c6890784e9156eeb8252906e72645f21aebf3dbf))
* **firmware:** calibrate restarts on r after printing the result ([d257f7e](https://github.com/owellnitz/plant-monitor/commit/d257f7e667880ccd844fd82a1edd9bf529268bcc))
* **firmware:** gate the fmt::Write import behind the net feature ([33b0cba](https://github.com/owellnitz/plant-monitor/commit/33b0cba6a71fccf382b1d5981014d785c07585b4))
* **firmware:** keep config.example.toml under the 1024-byte partition limit ([e4eef38](https://github.com/owellnitz/plant-monitor/commit/e4eef38fe49ffac83911b1e5b9febe40da28e705))
* **firmware:** keep the network when the display value is unknown ([e044e14](https://github.com/owellnitz/plant-monitor/commit/e044e14c20b5df07bb97265687c87fb3fed35f57))
* **firmware:** make Calibration fields private ([32835ce](https://github.com/owellnitz/plant-monitor/commit/32835cecadb33cb0a00528c66ad340d39f7e44c9))
* **firmware:** skip config lines without an equals sign ([94c5e9c](https://github.com/owellnitz/plant-monitor/commit/94c5e9c6bdfd312fbd524b3959908866e04a66c4))
* **firmware:** treat an unknown display value as headless ([3d43e2c](https://github.com/owellnitz/plant-monitor/commit/3d43e2c2fbbd712bfb463c873d3501949609d9d1))

## [0.7.0](https://github.com/owellnitz/plant-monitor/compare/firmware-v0.6.0...firmware-v0.7.0) (2026-09-04)


### Features

* **firmware:** report what the update attempt did ([1123def](https://github.com/owellnitz/plant-monitor/commit/1123def4514b515138537916e6a4cd9643765d98))

## [0.6.0](https://github.com/owellnitz/plant-monitor/compare/firmware-v0.5.1...firmware-v0.6.0) (2026-09-04)


### Features

* **firmware:** show a spinner while the reading settles ([f1ca81c](https://github.com/owellnitz/plant-monitor/commit/f1ca81c961cfafcd6ef15e4c44d77acad3bc2b5f))

## [0.5.1](https://github.com/owellnitz/plant-monitor/compare/firmware-v0.5.0...firmware-v0.5.1) (2026-09-03)


### Bug Fixes

* **firmware:** accept an update check with no Content-Length ([cb6f3c7](https://github.com/owellnitz/plant-monitor/commit/cb6f3c7dd5f40d8102ef7e64d118eb290ad021de))
* **firmware:** time out a stalled download, not a slow one ([3aff979](https://github.com/owellnitz/plant-monitor/commit/3aff9796893325d51da925c098a0a4bdf7c3d60d))

## [0.5.0](https://github.com/owellnitz/plant-monitor/compare/firmware-v0.4.0...firmware-v0.5.0) (2026-09-03)


### Features

* **firmware:** check for and install updates on the hourly wake ([290a8e5](https://github.com/owellnitz/plant-monitor/commit/290a8e5395ed2b51029b43f249a288038da518ee))
* **firmware:** confirm a new image so it is not rolled back ([2164892](https://github.com/owellnitz/plant-monitor/commit/216489294f65da4dd4abc35562f399bc1e949b8b))
* **firmware:** feed the watchdog while downloading ([3c98000](https://github.com/owellnitz/plant-monitor/commit/3c98000e9398cf2120e92ac632939e8f7106599e))
* **firmware:** parse the backend's update offer ([c536107](https://github.com/owellnitz/plant-monitor/commit/c536107ada5faf5950c49d763973f4c558ec0355))
* **firmware:** read a short response body in full ([f55b2dd](https://github.com/owellnitz/plant-monitor/commit/f55b2dd8a7d6e3dcf8c7ef9b811c37a2b76506e6))
* **firmware:** read the backend port from config ([a7f8098](https://github.com/owellnitz/plant-monitor/commit/a7f8098723e550b3b5e6e32158af7f3f872d23cc))
* **firmware:** stream a firmware image into a sink ([719f2d2](https://github.com/owellnitz/plant-monitor/commit/719f2d28952e7a261d46fe12fd5a7bf73c9d4c25))
* **firmware:** verify a downloaded image against its sha256 ([135e264](https://github.com/owellnitz/plant-monitor/commit/135e264655c5e98f46f56587c65145819d7ae0ca))
* **firmware:** write images by sector and swap the boot slot ([033b03d](https://github.com/owellnitz/plant-monitor/commit/033b03dc8e8e232901bff1bc105bcc7fbca01af4))

## [0.4.0](https://github.com/owellnitz/plant-monitor/compare/firmware-v0.3.0...firmware-v0.4.0) (2026-09-02)


### Features

* **firmware:** add an HTTP/1.0 GET client ([b158733](https://github.com/owellnitz/plant-monitor/commit/b15873368fe904715b70119aec7b25ad96ddb5ea))
* **firmware:** add config-partition provisioning script ([50ef61c](https://github.com/owellnitz/plant-monitor/commit/50ef61c8019b71d401d33d913aa629226d25cd98))
* **firmware:** add OTA partition table and firmware build id ([e9bc3ed](https://github.com/owellnitz/plant-monitor/commit/e9bc3ed3abc91545cecfb8daf06954cd6ff1af99))
* **firmware:** cover the HTTP client's failure paths ([52f594c](https://github.com/owellnitz/plant-monitor/commit/52f594c6457700e04efe5c3804fb2d380d2db8b7))
* **firmware:** parse Content-Length from the response ([b9ea6fb](https://github.com/owellnitz/plant-monitor/commit/b9ea6fba319c18a1f2bd5b61244b7230bea9cdc4))
* **firmware:** read WiFi/MQTT config from a flash partition ([caefe62](https://github.com/owellnitz/plant-monitor/commit/caefe62c629cff49846fa705058ed02613dd5e11))
* **firmware:** report the reset reason in each reading ([3489b57](https://github.com/owellnitz/plant-monitor/commit/3489b57790ee049bfc74f3156ca0b061a81d5d95))


### Bug Fixes

* **firmware:** recover from hangs instead of bricking until RST ([09f39d2](https://github.com/owellnitz/plant-monitor/commit/09f39d242ef472462fb898760dacc5d20033e37d))

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
