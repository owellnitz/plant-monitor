// std is linked only for host-side unit tests (`cargo test --lib`).
#![cfg_attr(not(test), no_std)]

pub mod config;
pub mod mqtt;
pub mod sensor;
