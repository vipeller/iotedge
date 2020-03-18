use std::future::Future;

use failure::ResultExt;
use futures_util::future::{self, Either, FutureExt};
use futures_util::pin_mut;
use futures_util::stream::StreamExt;
use tokio::{net::ToSocketAddrs, sync::oneshot};
use tracing::{debug, error, info, span, warn, Level};
use tracing_futures::Instrument;

use crate::broker::{Broker, BrokerHandle, BrokerState};
use crate::transport::TransportBuilder;
use crate::{connection, Error, ErrorKind, Message, SystemEvent};

#[derive(Default)]
pub struct Server {
    broker: Broker,
}

impl Server {
    pub fn new() -> Self {
        Self::from_broker(Broker::default())
    }

    pub fn from_broker(broker: Broker) -> Self {
        Self { broker }
    }

    pub async fn serve<A, F>(
        self,
        transports: Vec<TransportBuilder<A>>,
        shutdown_signal: F,
    ) -> Result<BrokerState, Error>
    where
        A: ToSocketAddrs,
        F: Future<Output = ()> + Unpin,
    {
        let Server { broker } = self;
        let mut handle = broker.handle();
        let broker_task = tokio::spawn(broker.run());

        let mut incoming_tasks = Vec::new();
        let mut incoming_tasks_tx = Vec::new();
        for transport in transports {
            let (itx, irx) = oneshot::channel::<()>();
            incoming_tasks_tx.push(itx);

            let incoming_task = incoming_task(transport, handle.clone(), irx.map(drop));

            let incoming_task = Box::pin(incoming_task);
            incoming_tasks.push(incoming_task);
        }

        pin_mut!(broker_task);

        let incoming_tasks = future::select_all(incoming_tasks);
        let main_task = future::select(broker_task, incoming_tasks);

        // Handle shutdown
        let state = match future::select(shutdown_signal, main_task).await {
            Either::Left((_, tasks)) => {
                info!("server received shutdown signal");

                // shutdown the incoming loop
                info!("shutting down accept loop...");

                debug!("sending stop signal for every protocol head");
                for itx in incoming_tasks_tx {
                    itx.send(()).unwrap();
                }

                match tasks.await {
                    Either::Right(((result, _index, unfinished_incoming_tasks), broker_task)) => {
                        // wait until the rest of incoming_tasks finished
                        let mut results = vec![result];
                        results.extend(future::join_all(unfinished_incoming_tasks).await);

                        for e in results.into_iter().filter_map(Result::err) {
                            warn!(message = "failed to shutdown protocol head", error=%e);
                        }

                        debug!("sending Shutdown message to broker");
                        handle.send(Message::System(SystemEvent::Shutdown)).await?;
                        broker_task.await.context(ErrorKind::TaskJoin)?
                    }
                    Either::Left((broker_state, incoming_tasks)) => {
                        warn!("broker exited before accept loop");

                        // wait until either of incoming_tasks finished
                        let (result, _index, unfinished_incoming_tasks) = incoming_tasks.await;

                        // wait until the rest of incoming_tasks finished
                        let mut results = vec![result];
                        results.extend(future::join_all(unfinished_incoming_tasks).await);

                        for e in results.into_iter().filter_map(Result::err) {
                            warn!(message = "failed to shutdown protocol head", error=%e);
                        }

                        broker_state.context(ErrorKind::TaskJoin)?
                    }
                }
            }
            Either::Right((either, _)) => match either {
                Either::Right(((result, index, unfinished_incoming_tasks), broker_task)) => {
                    debug!("sending Shutdown message to broker");

                    if let Err(e) = &result {
                        error!(message = "an error occurred in the accept loop", error=%e);
                    }

                    debug!("sending stop signal for the rest of protocol heads");
                    incoming_tasks_tx.remove(index);
                    for itx in incoming_tasks_tx {
                        itx.send(()).unwrap();
                    }

                    let mut results = vec![result];
                    results.extend(future::join_all(unfinished_incoming_tasks).await);

                    handle.send(Message::System(SystemEvent::Shutdown)).await?;

                    let broker_state = broker_task.await;

                    // todo refactor to possible gather all errors
                    if let Some(Err(e)) = results.into_iter().find(Result::is_err) {
                        return Err(e);
                    } else {
                        broker_state.context(ErrorKind::TaskJoin)?
                    }
                }
                Either::Left((broker_state, incoming_tasks)) => {
                    warn!("broker exited before accept loop");

                    debug!("sending stop signal for the rest of protocol heads");
                    for itx in incoming_tasks_tx {
                        itx.send(()).unwrap();
                    }

                    // wait until either of incoming_tasks finished
                    let (result, _index, unfinished_incoming_tasks) = incoming_tasks.await;

                    // wait until the rest of incoming_tasks finished
                    let mut results = vec![result];
                    results.extend(future::join_all(unfinished_incoming_tasks).await);

                    for e in results.into_iter().filter_map(Result::err) {
                        warn!(message = "failed to shutdown protocol head", error=%e);
                    }

                    broker_state.context(ErrorKind::TaskJoin)?
                }
            },
        };
        Ok(state)
    }
}

async fn incoming_task<A, F>(
    transport: TransportBuilder<A>,
    handle: BrokerHandle,
    mut shutdown_signal: F,
) -> Result<(), Error>
where
    A: ToSocketAddrs,
    F: Future<Output = ()> + Unpin,
{
    let mut io = transport.build().await?;
    let addr = io.address()?;
    let span = span!(Level::INFO, "server", listener=%addr);
    let _enter = span.enter();

    let mut incoming = io.incoming();

    info!("Listening on address {}", addr);

    loop {
        match future::select(&mut shutdown_signal, incoming.next()).await {
            Either::Right((Some(Ok(stream)), _)) => {
                let peer = stream
                    .peer_addr()
                    .context(ErrorKind::ConnectionPeerAddress)?;

                let broker_handle = handle.clone();
                let span = span.clone();
                tokio::spawn(async move {
                    if let Err(e) = connection::process(stream, peer, broker_handle)
                        .instrument(span)
                        .await
                    {
                        warn!(message = "failed to process connection", error=%e);
                    }
                });
            }
            Either::Left(_) => {
                info!(
                    "accept loop shutdown. no longer accepting connections on {}",
                    addr
                );
                break;
            }
            Either::Right((Some(Err(e)), _)) => {
                warn!("accept loop exiting due to an error - {}", e);
                break;
            }
            Either::Right((None, _)) => {
                warn!("accept loop exiting due to no more incoming connections (incoming returned None)");
                break;
            }
        }
    }
    Ok(())
}
