// std is linked only for host-side unit tests (`cargo test --lib`).
#![cfg_attr(not(test), no_std)]

pub mod config;
pub mod http;
pub mod mqtt;
pub mod ota;
pub mod sensor;
