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

import org.springframework.batch.core.step.skip.SkipLimitExceededException;
import org.springframework.batch.core.step.skip.SkipPolicy;
import software.amazon.awssdk.services.dynamodb.model.DynamoDbException;

/**
 * {@code DynamoDbLargeItemSkipPolicy} is a {@link SkipPolicy} implementation that checks if an
 * encountered exception is due to a DynamoDB item exceeding the allowed size.
 *
 * <p>When a {@link DynamoDbException} is detected in the exception's cause chain and its message
 * contains the text "Item size has exceeded the maximum allowed size", this policy directs the
 * framework to skip processing of the current item.
 *
 * @see org.springframework.batch.core.step.skip.SkipPolicy
 * @see DynamoDbException
 */
public class DynamoDbLargeItemSkipPolicy implements SkipPolicy {

  /**
   * Evaluates whether the processing should be skipped based on the provided exception.
   *
   * <p>This method traverses the cause chain of the given throwable. If it finds a cause that is an
   * instance of {@link DynamoDbException} and its message contains the substring "Item size has
   * exceeded the maximum allowed size", it returns {@code true} indicating that the exception
   * qualifies for a skip.
   *
   * @param t the throwable encountered during processing
   * @param skipCount the number of items that have already been skipped (not used in this
   *     implementation)
   * @return {@code true} if the exception is due to a DynamoDB item being too large; {@code false}
   *     otherwise
   * @throws SkipLimitExceededException if the number of allowable skips is exceeded
   */
  public boolean shouldSkip(Throwable t, long skipCount) throws SkipLimitExceededException {
    Throwable cause = t;
    while (cause != null) {
      if (cause instanceof DynamoDbException
          && cause.getMessage().contains("Item size has exceeded the maximum allowed size")) {
        return true;
      }
      cause = cause.getCause();
    }
    return false;
  }
}
