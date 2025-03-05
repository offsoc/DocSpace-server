// (c) Copyright Ascensio System SIA 2009-2025
//
// This program is a free software product.
// You can redistribute it and/or modify it under the terms
// of the GNU Affero General Public License (AGPL) version 3 as published by the Free Software
// Foundation. In accordance with Section 7(a) of the GNU AGPL its Section 15 shall be amended
// to the effect that Ascensio System SIA expressly excludes the warranty of non-infringement of
// any third-party rights.
//
// This program is distributed WITHOUT ANY WARRANTY, without even the implied warranty
// of MERCHANTABILITY or FITNESS FOR A PARTICULAR  PURPOSE. For details, see
// the GNU AGPL at: http://www.gnu.org/licenses/agpl-3.0.html
//
// You can contact Ascensio System SIA at Lubanas st. 125a-25, Riga, Latvia, EU, LV-1021.
//
// The  interactive user interfaces in modified source and object code versions of the Program must
// display Appropriate Legal Notices, as required under Section 5 of the GNU AGPL version 3.
//
// Pursuant to Section 7(b) of the License you must retain the original Product logo when
// distributing the program. Pursuant to Section 7(e) we decline to grant you any rights under
// trademark law for use of our trademarks.
//
// All the Product's GUI elements, including illustrations and icon sets, as well as technical
// writing
// content are licensed under the terms of the Creative Commons Attribution-ShareAlike 4.0
// International. See the License terms at http://creativecommons.org/licenses/by-sa/4.0/legalcode
package com.asc.transfer.policy;

import com.asc.transfer.entity.ClientDynamoEntity;
import com.asc.transfer.entity.ClientEntity;
import lombok.extern.slf4j.Slf4j;
import org.springframework.batch.core.SkipListener;

/**
 * {@code DynamoDbLargeItemSkipListener} implements the {@link SkipListener} interface to provide
 * custom logging when items are skipped during a Spring Batch job. This listener logs warnings for
 * skip events that occur during processing, reading, and writing of records.
 *
 * <p>For processing and writing events, it logs the client ID associated with the record along with
 * the error message.
 *
 * @see org.springframework.batch.core.SkipListener
 */
@Slf4j
public class DynamoDbLargeItemSkipListener
    implements SkipListener<ClientEntity, ClientDynamoEntity> {

  /**
   * Callback method invoked when an item is skipped during processing.
   *
   * <p>Logs a warning message that includes the client ID from the input item and the associated
   * error message.
   *
   * @param item the {@link ClientEntity} that was being processed when the error occurred
   * @param t the exception that triggered the skip
   */
  public void onSkipInProcess(ClientEntity item, Throwable t) {
    log.warn(
        "Skipping a record for clientId {} during the process due to an error: {}",
        item.getClientId(),
        t.getMessage());
  }

  /**
   * Callback method invoked when an item is skipped during reading.
   *
   * <p>Logs a warning message that includes the error message describing the issue encountered
   * during the read.
   *
   * @param t the exception that triggered the skip during the read phase
   */
  public void onSkipInRead(Throwable t) {
    log.warn("Skipping a record during the read due to an error: {}", t.getMessage());
  }

  /**
   * Callback method invoked when an item is skipped during writing.
   *
   * <p>Logs a warning message that includes the client ID from the output item and the associated
   * error message.
   *
   * @param item the {@link ClientDynamoEntity} that was being written when the error occurred
   * @param t the exception that triggered the skip
   */
  public void onSkipInWrite(ClientDynamoEntity item, Throwable t) {
    log.warn(
        "Skipping a record for clientId {} during the write due to an error: {}",
        item.getClientId(),
        t.getMessage());
  }
}
